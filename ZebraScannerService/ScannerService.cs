﻿using System;
using System.Collections.Generic;
//using System.Linq;
//using System.Text;
//using System.Threading.Tasks;
//using System.Timers;

using System.Threading;
using System.ComponentModel;
using Motorola.Snapi;
using Motorola.Snapi.Constants.Enums;
using Motorola.Snapi.Constants;
using Motorola.Snapi.EventArguments;

using System.Text.RegularExpressions;

using Renci.SshNet;
using System.Reflection;
using log4net;
using log4net.Config;


namespace ZebraScannerService
{
	static class BarcodeExtension
	{
		public static string GetDescription(this Enum value)
		{
			var fi = value.GetType().GetField(value.ToString());

			var attributes = (DescriptionAttribute[])fi.GetCustomAttributes(typeof(DescriptionAttribute), false);

			if ((attributes != null) && (attributes.Length > 0))
				return attributes[0].Description;
			return value.ToString();
		}
	}

	class ScannerService
	{
		//readonly System.Timers.Timer _timer;

		// last location scanned - turn this into object maybe
		private static string location;

		private static bool _scannerAttached;

		private static ConnectionInfo ConnInfo;

		private static readonly ILog _log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);

		// for each notification define a colour, length of flash, and beep pattern
		private static Dictionary<string, Tuple<LedMode, LedMode, int, BeepPattern?>> notifications = new Dictionary<string, Tuple<LedMode, LedMode, int, BeepPattern?>>();

		//public ScannerService()
		//{
		//	_timer = new System.Timers.Timer(1000) { AutoReset = true };
		//	_timer.Elapsed += (sender, eventArgs) => Console.WriteLine("It is {0} and all is well", DateTime.Now);
		//}

		public void Start()
		{
			// add logging functionality
			XmlConfigurator.Configure();

			//First open an instance of the CoreScanner driver.
			// Instance returns BarCodeScannerManager which has private CoreScanner driver. 
			// Then Open calls Open API (see Windows SDK) on the CoreScanner variable
			// I think it's called like this because BarcodeScannerManager is static (only 1 should exist)
			if (!(BarcodeScannerManager.Instance.Open()))
			{
				_log.Fatal("Failed to open CoreScanner driver");
			}
			else
			{

				_log.Debug("CoreScanner driver instance opened");
			}

			//// Setup SSH connection info for remote inventory database access
			//ConnInfo = new ConnectionInfo("inventory", "tunet",
			//	new AuthenticationMethod[] {
			//		// Password based Authentication
			//		new PasswordAuthenticationMethod("tunet","Pa$$word")
			//	}
			//);

			// Setup SSH connection info for remote inventory database access
			ConnectionInfo ConnInfo = new ConnectionInfo("jmorrison", "jmorrison",
				new AuthenticationMethod[] {
					// Password based Authentication
					new PasswordAuthenticationMethod("jmorrison","Pa$$wordjm")
				}
			);

			//_log.Debug("Added SSH connection info: tunet@inventory");

			//Get a List<IMotorolaBarcodeScanner> containing each connected scanner.
			// Calls GetScanner API and returns formatted list of scanners
			// scanners are datatype that have corescanner instance and info.
			//_scanners = BarcodeScannerManager.Instance.GetDevices();

			// Register for barcode events, by adding handler onBarcodeEvent to event BarcodeEvent.
			// BarcodeEvent is of type "event _ICoreScannerEvents_BarcodeEventEventHandler BarcodeEvent" (delegate defines signature)
			// onBarcodeData invokes DataReceived.
			// then call ExecCommand on CoreScanner internal

			// ***** check to see if I need other event types
			BarcodeScannerManager.Instance.RegisterForEvents(EventType.Barcode, EventType.Pnp);

			BarcodeScannerManager.Instance.ScannerAttached += Instance_ScannerAttached;
			BarcodeScannerManager.Instance.ScannerDetached += Instance_ScannerDetached;

			// add subscriber onDataReceived
			//BarcodeScannerManager.Instance.DataReceived += OnDataReceived;

			_log.Debug("Subscribed for events in BarcodeScannerManager: CCoreScanner.Barcode, CCoreScanner.Pnp");
			_log.Debug("Subscribed for events in Main: BarcodeScannerManager.ScannerAttached, BarcodeScannerManager.ScannerDetached");

			// for some reason can't put values directly into tuples.
			BeepPattern? beeper = BeepPattern.ThreeHighShort;
			BeepPattern? beeper2 = null;
			BeepPattern? beeper3 = BeepPattern.ThreeLowShort;
			BeepPattern? beeper4 = BeepPattern.OneHighShort;

			// can add device in use notification
			notifications.Add("barcodeFailure", Tuple.Create(LedMode.YellowOn, LedMode.YellowOff, 300, beeper));
			notifications.Add("barcodeSuccess", Tuple.Create(LedMode.GreenOn, LedMode.GreenOff, 150, beeper4));
			notifications.Add("databaseFailure", Tuple.Create(LedMode.RedOn, LedMode.RedOff, 300, beeper3));
			notifications.Add("databaseSuccess", Tuple.Create(LedMode.GreenOn, LedMode.GreenOff, 150, beeper2));

			//invoke when program starts, method also invoked whenever a new scanner is connected
			ConnectScanners();

			Console.WriteLine("ready to scan");
			//Console.ReadLine();
			//_timer.Start();
		}

		public void Stop() {
			//_timer.Stop();
		}

		private static void Instance_ScannerAttached(object sender, PnpEventArgs e)
		{
			_scannerAttached = true;
			// change the scanner mode if necessary
			//ConnectScanners();
			// update the global scanner list
			//_scanners = BarcodeScannerManager.Instance.GetDevices();

			// log scanner attached
			Console.WriteLine("Scanner id=" + e.ScannerId + " attached");
		}

		private static void Instance_ScannerDetached(object sender, PnpEventArgs e)
		{
			_scannerAttached = false;
			// update the global scanner list
			//_scanners = barcodescannermanager.instance.getdevices();

			// improve logging
			Console.WriteLine("Scanner id=" + e.ScannerId + " detached");

			// can add persistent red if scanner becomes detached??? Or is it gone
		}

		private static void ConnectScanners()
		{
			foreach (var scanner in BarcodeScannerManager.Instance.GetDevices())
			{
				scanner.SetHostMode(HostMode.USB_OPOS, true);
				_log.Debug("Scanner id=" + scanner.Info.ScannerId + " set to USB OPOS mode");

				if (scanner.Info.UsbHostMode != HostMode.USB_OPOS)
				{
					_log.Error("Failed to set scanner id=" + scanner.Info.ScannerId + " To USB OPOS mode. Retrying...");
					scanner.SetHostMode(HostMode.USB_OPOS, true);

					// not sure why this is here... perhaps for connection issues.
					// does this need to be improved? eg scanner A attached then scanner B becomes detached before this line gets executed. sleep forever?
					while (_scannerAttached == false)
					{
						Thread.Sleep(3000);
					}
				}

				//scanner.Defaults.Restore();
				//scanner.CaptureMode = CaptureMode.Barcode;
				//GetAttributes(scanner);

				//PerformCommands(scanner);
				//scanner.Trigger.TriggerByCommand = true;
				//scanner.Trigger.PullTrigger();

				scanner.Actions.SetAttribute(138, 'B', 0); // sound beeper via attribute
			}
		}

		// handles data 
		private static void OnDataReceived(object sender, BarcodeScanEventArgs e)
		{
			// is it possible to define a new type of barcode?? Could avoid regular expressions altogether

			_log.Debug("Barcode scan detected from scanner " + e.ScannerId + ": " + e.Data);

			Console.WriteLine("Barcode type: " + e.BarcodeType.GetDescription());
			Console.WriteLine("Data: " + e.Data);

			// convert barcode to uppercase and strip any whitespace
			string barcode = e.Data.ToUpper().Trim();

			string barcodeType = CheckBarcode(barcode);

			if (string.IsNullOrEmpty(barcodeType) || barcodeType.Equals("nid") && string.IsNullOrEmpty(location))
			{
				_log.Error("Barcode " + e.Data + " not recognized as location or NID");
				SendNotification(e.ScannerId, notifications["barcodeFailure"]);
			}
			else
			{
				_log.Debug("Barcode " + barcode + " recognized as type " + barcodeType);
				SendNotification(e.ScannerId, notifications["barcodeSuccess"]);

				if (barcodeType.Equals("location"))
				{
					location = barcodeType;
				}
				// barcode is NID and location has been previously entered - update database
				else
				{
					SendNotification(e.ScannerId, notifications["databaseSuccess"]);
					UpdateDatabase(e.ScannerId, location, barcode);

					// set location to null - shouldn't be allowed to have 2 nids at same location
					// fix this to account for programming lab ????????
					location = null;
				}
			}
		}

		// returns "nid" if barcode scanned is recognized as NID, and "location" if recognized as location
		public static string CheckBarcode(string barcode)
		{
			string locationOrNid = "";
			string locationFormat = @"^P[NESW]\d{4}";
			string nidFormat = @"^\d{10}$";

			if (EvalRegex(locationFormat, barcode))
			{
				locationOrNid = "location";
			}
			else if (EvalRegex(nidFormat, barcode))
			{
				locationOrNid = "nid";
			}
			return locationOrNid;
		}

		public static Boolean EvalRegex(string rxStr, string matchStr)
		{
			Regex rx = new Regex(rxStr);
			Match match = rx.Match(matchStr);

			return match.Success;
		}

		public static void UpdateDatabase(uint scannerId, string location, string nid)
		{
			// ************ consider passing timestamp as well so that there is consistency between logger and inventory ************

			// Execute a (SHELL) Command that runs python script to update database
			using (var sshclient = new SshClient(ConnInfo))
			{
				sshclient.Connect();
				//using (var cmd = sshclient.CreateCommand("python3 /var/www/scripts/autoscan.py" + location + " " + nid))
				using (var cmd = sshclient.CreateCommand("python3 /home/jmorrison/scanning-project/autoscan/dbtester.py " + location + " " + nid))
				{
					cmd.Execute();
					Console.WriteLine("Command>" + cmd.CommandText);
					Console.WriteLine("Return Value = {0}", cmd.ExitStatus);

					// consider adding different return codes
					if (cmd.ExitStatus != 0)
					{
						// send notification from here so it's faster
						SendNotification(scannerId, notifications["databaseFailure"]);
						_log.Fatal("Error connecting to database or updating database with location=" + location + ", NID=" + nid);
					}
					else
					{
						// **** fix this to ensure actually successful database update from autoscan.py
						_log.Debug("Successfully updated database with location=" + location + ", NID=" + nid);
					}
				}
				sshclient.Disconnect();
			}
		}

		public static void SendNotification(uint scannerId, Tuple<LedMode, LedMode, int, BeepPattern?> notificationParams)
		{
			IMotorolaBarcodeScanner scanner = GetScannerFromId(scannerId);

			// sound beeper
			if (notificationParams.Item4 != null)
			{
				scanner.Actions.SoundBeeper((BeepPattern)notificationParams.Item4);
			}
			// flash LED
			scanner.Actions.ToggleLed(notificationParams.Item1);
			Thread.Sleep(notificationParams.Item3);
			scanner.Actions.ToggleLed(notificationParams.Item2);
		}

		//// need to figure out which scanner the scan came from
		//public static void SendFailureNotification(uint scannerId)
		//{
		//	IMotorolaBarcodeScanner scanner = GetScannerFromId(scannerId);

		//	// if scanner DNE in list something has gone wrong. Rather try/catch then if null. 

		//	// send beep and flash indicating that the scan is not correct
		//	scanner.Actions.SoundBeeper(BeepPattern.TwoHighShort);

		//	// flash LED
		//	scanner.Actions.ToggleLed(LedMode.YellowOn);
		//	Thread.Sleep(1000);
		//	scanner.Actions.ToggleLed(LedMode.YellowOff);
		//}

		//// need to figure out which scanner the scan came from
		//public static void SendDatabaseNotification(uint scannerId)
		//{
		//	IMotorolaBarcodeScanner scanner = GetScannerFromId(scannerId);

		//	// if scanner DNE in list something has gone wrong. Rather try/catch then if null. 

		//	// flash LED
		//	scanner.Actions.ToggleLed(LedMode.GreenOn);
		//	Thread.Sleep(1000);
		//	scanner.Actions.ToggleLed(LedMode.GreenOff);
		//}

		public static IMotorolaBarcodeScanner GetScannerFromId(uint scannerId)
		{
			foreach (var scanner in BarcodeScannerManager.Instance.GetDevices())
			{
				if (scanner.Info.ScannerId == scannerId)
				{
					return scanner;
				}
			}
			return null;
		}
	}
}
