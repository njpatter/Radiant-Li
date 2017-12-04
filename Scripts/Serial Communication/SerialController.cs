using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;

using System.Diagnostics;

/// <summary>
/// Sends and receives data via the serial port.
/// </summary>
/// <description>
/// Uses three threads:
/// 	- Initialization thread. Connects to the propeller,
/// 	  pings, and checks versions. If necessary, updates
/// 	  the firmware. Spawns the read & write threads when
/// 	  finished.
///		- Read thread. Reads incoming bytes. Writes completed
/// 	  lines to the synchronized read lines buffer. Places
///		  results in a Dictionary for later reading. This provides
/// 	  a chance to check for errors if we try to place a string
/// 	  into a Dictionary's value and one already exists…
/// 	- Write thread. Writes outgoing bytes from a synchronized
/// 	  buffer. 
///  Two buffers:
/// 	- Read lines. Complete lines of text for the program.
/// 	- Bytes to write. Sends in data mode.
/// </description>
public class SerialController : MonoBehaviour, ICommandConsumer {
	#if !UNITY_IOS && !UNITY_ANDROID
	#region Public interface
	#region Public constants
	/// <summary>
	/// Sent when a connection is successfully established.
	/// </summary>
	public const string kOnSerialConnectionEstablished = "OnSerialConnectionEstablished";

	/// <summary>
	/// The serial port baud rate.
	/// </summary>
	public const int kBaudRate = 115200;

	/// <summary>
	/// The default input buffer size.
	/// </summary>
	const int kSerialRxBufferSize = 1024;

	/// <summary>
	/// Default loop timeout.
	/// </summary>
	public const int kLoopTimeout = 10;

	/// <summary>
	/// How long a callback can survive before raising an 
	/// error when not receiving a response.
	/// </summary>
	public const int kDefaultCallbackTimoutInSeconds = 30;

	/// <summary>
	/// Suggested timeout in seconds for calibration.
	/// </summary>
	public const int kCalibrationTimeoutInSeconds = 5 * 60;

	public const int kSecToMs = 1000;
	#endregion

	const float kOpenDelay = 4.0f;

	#region General interface
	public TextAsset versionAsset;

	public bool isConnected { get { return m_serialPort.isConnected && m_foundPrinter; } } 

	/// <summary>
	/// Clears any pending packets. Note does not clear currently 
	/// being transmitted packets.
	/// </summary>
	/// <remarks>
	/// Called by the main thread.
	/// </remarks>
	public void ClearTxBuffer() {
		m_packetSemaphore.WaitOne();
		m_dataToTransmit.Clear();
		m_dataBeingTransmitted.Clear();
		m_packetSemaphore.Release();
	}
	#endregion

	#region ICommandConsumer interface
	/// <summary>
	/// Gets a value indicating whether this <see cref="ICommandConsumer"/> provides feedback.
	/// </summary>
	/// <value>
	/// <c>true</c> if provides feedback; otherwise, <c>false</c>.
	/// </value>
	public bool providesFeedback { get { return true; } }

	/// <summary>
	/// Returns the number of packets still queued.
	/// </summary>
	/// <value>The number of unsent packets.</value>
	public int packetsRemaining { 
		get {
			int result;
			m_packetSemaphore.WaitOne();
			result = m_dataToTransmit.Count + m_dataBeingTransmitted.Count;
			Text.Log("Packets remaining: {0}", result);
			m_packetSemaphore.Release();
			return result;
		}
	}
	
	/// <summary>
	/// Returns the last temperature of the
	/// requested heater.
	/// </summary>
	/// <returns>The temperature of heater #index.</returns>
	/// <param name="index">The heater index, 0-based.</param>
	public int HeaterTemp(int index) {
		return index == 0 ? m_lastHeater0Temperature : m_lastHeater1Temperature;
	}

	/// <summary>
	/// Returns the bytes available to receive.
	/// </summary>
	/// <value>The rx bytes available.</value>
	public int rxBytesAvailable { get { return m_lastBufferAvailable; } }
	
	/// <summary>
	/// Returns the number of motor queue slots open.
	/// </summary>
	/// <value>The motor queue slots available.</value>
	public int motorQueueAvailable { get { return m_lastMotorQueueAvailable; } }

	
	/// <summary>
	/// Returns whether or not the consumer is seeking.
	/// </summary>
	/// <value><c>true</c> if is seeking; otherwise, <c>false</c>.</value>
	public bool isSeeking { get { return m_isSeeking; } }

	/// <summary>
	/// Prepare to send packets.
	/// </summary>
	public void BeginSendingPackets() {
		m_packetSemaphore.WaitOne();
		m_lockAcquired = true;
	}
	
	/// <summary>
	/// Finished sending packets for now.
	/// </summary>
	public void EndSendingPackets() {
		m_lockAcquired = false;
		m_packetSemaphore.Release();
	}

	/// <summary>
	/// Queues a packet to send.
	/// </summary>
	/// <param name='aCommand'>
	/// A command.
	/// </param>
	public void SendPacket(GanglionCommand aCommand) {
		SendPacket(new TxPacket(aCommand, 0));
	}
	
	/// <summary>
	/// Queues a packet to send.
	/// </summary>
	/// <param name='aCommand'>
	/// A command.
	/// </param>
	/// <param name='anArg'>
	/// An argument.
	/// </param>
	public void SendPacket(GanglionCommand aCommand, int anArg) {
		SendPacket(new TxPacket(aCommand, anArg));
	}
	
	/// <summary>
	/// Sends a pre-made packet.
	/// </summary>
	/// <param name='datum'>
	/// Datum.
	/// </param>
	public void SendPacket(TxPacket datum) {
		Contract.Assert(m_lockAcquired, @"Lock not acquired to write data; call BeginSendingPackets and EndSendingPackets.");
		m_dataToTransmit.Enqueue(datum);
	}
	#endregion
	
	#region Unity interface
	/// <summary>
	/// Initialize variables.
	/// </summary>
	public void Awake() {
#if DEBUG_TX || DEBUG_RX
		m_dataPath = Application.persistentDataPath;
#endif
		m_packetSemaphore = new Semaphore(1, 1);
		m_dispatchSemaphore = new Semaphore(1, 1);

		m_lockAcquired   = false;
		m_foundPrinter   = false;

		m_dataToTransmit           = new Queue<TxPacket>(Printer.kMotorQueueSize);
		m_dataBeingTransmitted     = new Queue<TxPacket>(Printer.kMotorQueueSize);

		#if UNITY_STANDALONE_WIN || UNITY_METRO
			m_serialPort = new WinSerialPort(kSerialRxBufferSize);
		#else
			m_serialPort = new OsxSerialPort(kSerialRxBufferSize);
		#endif
	}

	/// <summary>
	/// Starts the coroutines for sending and receiving data.
	/// </summary>
	void OnEnable() {
		if (!m_foundPrinter) StartCoroutine(FindPrinter(m_serialPort));
	}
	
	/// <summary>
	/// Stops coroutines and releases the printer.
	/// </summary>
	void OnDisable() {
		m_threadsActive = false;
		StopAllCoroutines();
		if (m_serialPort != null && m_serialPort.isConnected) {
			ResetPropeller();
			m_serialPort.Close();
		}
		m_foundPrinter = false;
		
		if (m_txThread != null) m_txThread.Abort();
		if (m_rxThread != null) m_rxThread.Abort();
	}

	/// <summary>
	/// Finds a printer using the specified serial connection.
	/// </summary>
	/// <returns>
	/// An enumerator for a coroutine.
	/// </returns>
	/// <param name='aSerialPort'>
	/// A serial port connection.
	/// </param>
	IEnumerator FindPrinter(ISerialPort aSerialPort) {
		m_foundPrinter = false;
		int timeout = kLoopTimeout;

		while (!aSerialPort.isConnected && timeout-- > 0) {
			string[] availablePorts = m_serialPort.AvailablePorts();
			Text.Log(@"Found {0} available port{1}.", availablePorts.Length, 
			         Text.S(availablePorts.Length));
			
			foreach (string aPortPath in availablePorts) {
				Text.Log(@"Trying to open {0}", aPortPath);
				
				bool success = false;
				try {
					success = aSerialPort.OpenPort(aPortPath, kBaudRate);
					Text.Log("Opened {0} at {1}.", aPortPath, kBaudRate);
				}
				catch (System.Exception e) {
					Text.Error(e);
					continue;
				}
				
				if (success) {
					// Unity reboots the Propeller on OSX but not on Windows.
					yield return StartCoroutine(ResetCoroutine());

					// We're in text mode upon startup, so try pinging.
					aSerialPort.Write("ping ");
					// Not blocking, so wait a bit.
					yield return new UnityEngine.WaitForSeconds(1.0f);//0.02f);
					string response = "(null)";
					int numRead = aSerialPort.Read(8, out response);
					response = response.Trim();
					Text.Log("Received {0} byte{1}: {2}", numRead, Text.S(numRead), response);

					if (response.Contains("pong")) {
						yield return StartCoroutine(CheckVersion(aSerialPort));

						m_foundPrinter = m_wasProgrammingSuccessful;
						if (m_foundPrinter) {
							Text.Log("Connected to " + aPortPath);
							aSerialPort.Write("data ");

							m_threadsActive = true;
							m_txThread = new Thread(TransmitData);
							m_txThread.Name = "Tx Thread";
							m_txThread.Start();

							m_rxThread = new Thread(ReceiveData);
							m_rxThread.Name = "Rx Thread";
							m_rxThread.Start();

							Dispatcher.Broadcast(kOnSerialConnectionEstablished);
						}
						yield break;
					}
					aSerialPort.Close();
					yield return null;
				}
			}
			yield return new WaitForSeconds(1);
		}
		if (timeout <= 0) {
			Text.Log(@"Couldn't find printer.");
			enabled = false;
		}
	}

	IEnumerator ResetCoroutine() {
		m_serialPort.RaiseDtr();
		yield return new UnityEngine.WaitForSeconds(0.025f);
		m_serialPort.ClearDtr();
		yield return new UnityEngine.WaitForSeconds(kOpenDelay);
	}

	static readonly char[] kSpinning = new char[] { '/', '-', '\\', '|' };
	/// <summary>
	/// Validates version, burning the bundled firmware if needed.
	/// </summary>
	/// <returns>The version.</returns>
	/// <param name="aPort">A port.</param>
	IEnumerator CheckVersion(ISerialPort aPort) {
		m_portPath = m_serialPort.OpenPortPath();
#if UNITY_STANDALONE_WIN || UNITY_WP8
		int comPortNumber;
#endif
		aPort.Write(@"version? ");
		yield return new UnityEngine.WaitForSeconds(0.05f);
		string versionString;
		aPort.Read(16, out versionString);
		versionString = versionString.Trim();

		UnityEngine.Debug.Log("Firmware Version from Propeller = \"" + versionString + "\"");
		UnityEngine.Debug.Log("Firmware Version in Li = \"" + versionAsset.text + "\"");

		if (versionString.Contains(versionAsset.text.Trim())) {
			Text.Log(@"Firmware version {0}.", versionString);
			m_wasProgrammingSuccessful = true;
		}
#if UNITY_STANDALONE_WIN || UNITY_WP8
		else if (int.TryParse(m_portPath.Substring(3), out comPortNumber) 
		         && comPortNumber > 9) 
		{
			string message = string.Format(@"Update failed: Try removing unused/hidden com ports in Device Manager & restart Li.");
			Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, message);
			Text.Log("Aborting after detecting {0} com ports.", comPortNumber);
			yield return new WaitForSeconds(20.0f);
			enabled = false;
			Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Message);
		}
#endif
		else {
			m_serialPort.Close();

			string message = string.Format(@"Burning firmware v. {0} /", versionAsset.text.Trim());
			Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, message);
			Text.Log("{0} over {1}", message, versionString);

			int timeout = 3;
			int spinIndex = 0;
			while (timeout-- > 0) {
				m_programmingThread = new Thread(new ParameterizedThreadStart(ProgramEeprom));
				m_programmingThread.Start(Application.streamingAssetsPath + @"/Firmware/");//encodedBinary);
				int programmingTimeoutSec = 60;
				while (m_programmingThread.IsAlive && programmingTimeoutSec-- > 0) {
					yield return new WaitForSeconds(1.0f);
					message = message.Remove(message.Length - 1);
					message += kSpinning[++spinIndex % kSpinning.Length];
					Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, message);
				}
				if (programmingTimeoutSec < 1) {
					Text.Log("Aborting programming thread; passed {0}", m_programmingError);
					m_programmingThread.Abort();
				}
				m_programmingThread = null;

				Text.Log("Burning result: {0}", m_programmingError);
				if (m_wasProgrammingSuccessful) {
					message = string.Format(@"Burning successful.");
					Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, message);
					Text.Log(message);
					yield return new WaitForSeconds(1.0f);
					Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Message);
					break;
				}
				else {
					message = string.Format(@"Still burning {0}", kSpinning[++spinIndex % kSpinning.Length]);
					Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, message);
					Text.Log(message);

					yield return new WaitForSeconds(0.5f);
				}
			}
			if (timeout <= 0) {
				message = string.Format(@"Burning failed. Try closing Li, rebooting the printer, and restarting.");
				Dispatcher<string>.Broadcast(PanelController.kEventShowMessage, message);
				Text.Log(message);
				yield return new WaitForSeconds(1.0f);
				enabled = false;
				Dispatcher<Panel>.Broadcast(PanelController.kEventClosePanel, Panel.Message);
			}
			m_serialPort.OpenPort(m_portPath, kBaudRate);
		}
	}
	#endregion
	#endregion

	#region Shared resources
	/// <summary>
	/// Filled by the main thread to send packets.
	/// </summary>
	Queue<TxPacket> m_dataToTransmit;
	
	/// <summary>
	/// Drained by the write thread.
	/// </summary>
	Queue<TxPacket> m_dataBeingTransmitted;

	/// <summary>
	/// Whether or not we've received a correct response from
	/// a device we've connected to.
	/// </summary>
	bool m_foundPrinter;
	public bool foundPrinter {
		get { return m_foundPrinter; }
	}
	
	/// <summary>
	/// The programming thread.
	/// </summary>
	Thread m_programmingThread;

	/// <summary>
	/// Semaphore for packet response callbacks.
	/// </summary>
	Semaphore m_dispatchSemaphore;

	/// <summary>
	/// The thread receiving data.
	/// </summary>
	Thread m_rxThread;

	/// <summary>
	/// The serial port to use.
	/// </summary>
	ISerialPort m_serialPort;

	/// <summary>
	/// Whether or not the threads are active.
	/// </summary>
	bool m_threadsActive;

	/// <summary>
	/// Ensures we have the semaphore for sending data. 
	/// </summary>
	bool m_lockAcquired;
	
	/// <summary>
	/// Syncronize access to the data to write buffer.
	/// </summary>
	Semaphore m_packetSemaphore;

	/// <summary>
	/// The thread sending data.
	/// </summary>
	Thread m_txThread;
	#endregion

	#region Programming Thread
	#region Methods
	bool m_wasProgrammingSuccessful;
	string m_programmingError;
	string m_portPath;

	void ProgramEeprom(System.Object boxedAssetsPath) {
		Process p = null;
		string stderr = null;
		string stdout = null;

		try {
			m_programmingError = @"Unknown error.";
			m_wasProgrammingSuccessful = false;

			string assetPath = (string)boxedAssetsPath;
			// NOTE: The filename needs to be the complete path to the executable.
			//string loader    = string.Format(@"{0}bstl", assetPath);
			string loader    = string.Format(@"{0}propeller-load", assetPath);
			string arguments;
#if UNITY_STANDALONE_WIN || UNITY_WP8
			loader += ".exe";
			arguments = string.Format("{0} Ganglion.binary -p3", m_portPath);
#else
			arguments = string.Format("-p {0} -e -r Ganglion.binary", m_portPath);
#endif
			// = string.Format(@"-d {0} -p 3 Ganglion.binary", m_portPath);
			//string arguments = string.Format(@"{0} Ganglion.binary", m_portPath);

			for (int i = 0; i < 5; ++i) {
				ProcessStartInfo startInfo = new ProcessStartInfo(loader, arguments) {
					WorkingDirectory      	= assetPath,
					RedirectStandardError 	= true,
					RedirectStandardOutput	= true,
					UseShellExecute       	= false,						// NOTE: Required to read stderr.
					WindowStyle				= ProcessWindowStyle.Hidden,	// Shell...still appears? WTF?
					CreateNoWindow			= true
				};
				p = Process.Start(startInfo);

				// NOTE: Must ReadToEnd prior to waiting for the exit; otherwise,
				// we can get deadlocks.
				Thread.Sleep(2 * 1000);
				stderr = p.StandardError.ReadToEnd();
				stdout = p.StandardOutput.ReadToEnd();
				m_programmingError = string.Format("stderr: {0}\nstdout: {1}", stderr, stdout);
				if (!p.WaitForExit(58 * 1000)) {
					p.Kill();
					m_programmingError = "Burning timed out: ";
					return;
				}

				int exitCode = p.ExitCode;
				if (exitCode == 0) {
					m_wasProgrammingSuccessful = true;
					break;
				}
			}
		}
		catch (System.Exception e) {
			m_programmingError = string.Format("{0}\nException: {1}", m_programmingError, e);
			if (p != null && !p.HasExited) {
				p.Kill();
			}
		}
	}

	void ResetPropeller() {
		m_serialPort.RaiseDtr();
		Thread.Sleep(25);	// Not used in PropellerLoader.spin
		m_serialPort.ClearDtr();
		Thread.Sleep(25);
		m_serialPort.RaiseDtr();
		Thread.Sleep(25);	// Not used in PropellerLoader.spin
		m_serialPort.ClearDtr();

		Thread.Sleep(90);
	}
	#endregion

	#endregion

	#region Read thread

	#region Read constants
	const int kReadQueueSize = 256;
	const int kReadQueueMask = kReadQueueSize - 1;
	#endregion

	#region Read variables
	int  m_lastHeater0Temperature;
	int  m_lastHeater1Temperature;
	int  m_lastBufferAvailable;
	int  m_lastMotorQueueAvailable;
	bool m_isSeeking;
	#endregion
	
	#region Read methods
	void ReceiveData() {
		byte[] rxBuffer      = new byte[kReadQueueSize];
		byte[] workingBuffer = new byte[kReadQueueSize];
		int wbStartIdx = 0;
		int wbEndIdx   = 0;
		int wbLength   = 0;

		RxLog("Creating rx log.");
		while (m_threadsActive) {
			try {
				// Firmware sends data ~1/sec.
				Thread.Sleep(kSecToMs);

				int numRead = m_serialPort.Read(kReadQueueSize, ref rxBuffer);
				RxLog("Read {0} byte{1}.", numRead, Text.S(numRead));

				if (numRead < 0) {
					RxLog("Read error: {0}", m_serialPort.error);
					continue;
				}

				int rxIndex = 0;
				while (numRead > 0) {
					workingBuffer[wbEndIdx] = rxBuffer[rxIndex++];
					wbEndIdx = (wbEndIdx + 1) & kReadQueueMask;
					wbLength++;
					numRead--;
				}
				RxLog("Buffer: {0}-{1}; {2} bytes.", 
				      wbStartIdx, wbEndIdx, wbLength);

				int checksum;
				int heater0;
				int heater1;
				int bufferAvailableLow;
				int bufferAvailableHigh;
				int motorQueueLow;
				int motorQueueHigh;

				bool foundCompletePacket = false;
				int validHeater0 = 0;
				int validHeater1 = 0;
				int validBufferAvailable = 0;
				int validMotorQueueAvailable = 0;
				// NOTE(kevin): We only want to use the last packet's
				// information; if we have partial packets or invalid 
				// packets, then we need to discard all the data
				// to ensure we don't overwrite data on the prop.
				while (wbLength >= 8) {
					// Find the first #
					while (wbLength > 0 && workingBuffer[wbStartIdx] != '#') {
						RxLog("Ignoring {0} (0x{1:x2}) at {2}", 
						      (char)workingBuffer[wbStartIdx],
						      (int) workingBuffer[wbStartIdx],
						      wbStartIdx);
						wbStartIdx = (wbStartIdx + 1) & kReadQueueMask;
						wbLength--;
					}

					// Not enough data? Break out.
					if (wbLength < 8) {
						RxLog("Working buffer of {0} too small.", wbLength);
						foundCompletePacket = false;
						break;
					}

					// We're at a #; invalidate the packet and extract its contents.
					workingBuffer[wbStartIdx] = 0;
					checksum            = workingBuffer[(wbStartIdx + 1) & kReadQueueMask];
					heater0             = workingBuffer[(wbStartIdx + 2) & kReadQueueMask];
					heater1             = workingBuffer[(wbStartIdx + 3) & kReadQueueMask];
					bufferAvailableLow  = workingBuffer[(wbStartIdx + 4) & kReadQueueMask];
					bufferAvailableHigh = workingBuffer[(wbStartIdx + 5) & kReadQueueMask];
					motorQueueLow       = workingBuffer[(wbStartIdx + 6) & kReadQueueMask];
					motorQueueHigh      = workingBuffer[(wbStartIdx + 7) & kReadQueueMask];
					if (checksum == ((heater0 + heater1 
					                  + bufferAvailableLow + bufferAvailableHigh 
					                  + motorQueueLow + motorQueueHigh) & 0xFF)) 
					{
						validHeater0 = heater0;
						validHeater1 = heater1;
						validBufferAvailable = bufferAvailableLow + (bufferAvailableHigh << 8);
						validMotorQueueAvailable = motorQueueLow + ((motorQueueHigh & 0x7F) << 8);
						foundCompletePacket = true;

						// NOTE(kevin): This is a safety precaution.
						workingBuffer[      (wbStartIdx + 1) & kReadQueueMask]
							= workingBuffer[(wbStartIdx + 2) & kReadQueueMask]
							= workingBuffer[(wbStartIdx + 3) & kReadQueueMask]
							= workingBuffer[(wbStartIdx + 4) & kReadQueueMask]
							= workingBuffer[(wbStartIdx + 5) & kReadQueueMask]
							= workingBuffer[(wbStartIdx + 6) & kReadQueueMask]
							= workingBuffer[(wbStartIdx + 7) & kReadQueueMask]
							= 0;

						wbStartIdx = (wbStartIdx + 8) & kReadQueueMask;
						wbLength -= 8;
					}
					else {
						RxLog("Invalid packet!");
						foundCompletePacket = false;
						wbStartIdx = (wbStartIdx + 1) & kReadQueueMask;
						wbLength--;
					}
				}

				if (foundCompletePacket) {
					RxLog("H0: {0}C; H1: {1}C; Rx Buffer: {2}; Motor Queue: {3}",
					      validHeater0, validHeater1, validBufferAvailable, validMotorQueueAvailable);
					m_dispatchSemaphore.WaitOne();
					m_lastHeater0Temperature  = validHeater0;
					m_lastHeater1Temperature  = validHeater1;
					m_lastBufferAvailable     = validBufferAvailable;
					m_lastMotorQueueAvailable = validMotorQueueAvailable;
					m_isSeeking               = false;
					m_dispatchSemaphore.Release();
				}
				else {
					// Set to safe values.
					RxLog("Zeroing & setting IsSeeking");
					m_dispatchSemaphore.WaitOne();
					m_lastBufferAvailable     = 0;
					m_lastMotorQueueAvailable = 0;
					m_isSeeking               = true;
					m_dispatchSemaphore.Release();
				}
			}
			catch (System.Threading.ThreadAbortException) { 
				break;
			}
			catch (System.Exception e) {
				RxLog(e.ToString());
			}
		}
		RxLog("Closing.");
	}
	#endregion
	#endregion

	#region Write thread

	#region Write constants
	/// <summary>
	/// The minimum bytes required in the general receive buffer. 
	/// </summary>
	public const int kMinRxBufferSpaceRequired = 4096;
	#endregion

	#region Methods
	void TransmitData() {
		byte[] txBuffer = new byte[Printer.kRxBufferSize];
		int bytesAvailable = 0;

		TxLog("Starting tx loop.");
		while (m_threadsActive) {
			try {
				while (m_threadsActive && bytesAvailable < kMinRxBufferSpaceRequired) {
					// Ensure that the RX buffer updates.
					Thread.Sleep(kSecToMs * 2);

					// Update our bytes free before sending data.
					m_dispatchSemaphore.WaitOne();
					bytesAvailable        = m_lastBufferAvailable;
					m_lastBufferAvailable = 0;
					m_dispatchSemaphore.Release();
				} 

				TxLog("Bytes available: {0}.", bytesAvailable);
				while (m_threadsActive && m_dataBeingTransmitted.Count < 1) {
					bool swapped = false;
					m_packetSemaphore.WaitOne();
					if (m_dataToTransmit.Count > 0) {
						TxLog("Acquired new tx buffer.");
						swapped = true;
						Queue<TxPacket> temp   = m_dataToTransmit;
						m_dataToTransmit       = m_dataBeingTransmitted;
						m_dataBeingTransmitted = temp;
					}
					m_packetSemaphore.Release();

					// NOTE(kevin): This is where we'll spend most of our
					// time waiting for commands. 200ms is approx. the 
					// speed of a double click.
					if (!swapped) {
						Thread.Sleep(200);
					}
				}

				int numTxBytes = 0;
				TxLog("Packets pending: {0}", m_dataBeingTransmitted.Count);
				while (   m_threadsActive 
				       && m_dataBeingTransmitted.Count > 0 
				       && bytesAvailable >= kMinRxBufferSpaceRequired) 
				{
					TxPacket aPacket = m_dataBeingTransmitted.Dequeue();
					
					// Figure out what we need to send.
					switch (aPacket.aCmd) {
						case GanglionCommand.Value:
							int aValue = aPacket.anArgument;
							if (aValue <= 127) {
								aValue += 128;
								txBuffer[numTxBytes++] = (byte)aValue;
								bytesAvailable--;
							}
							else if (aValue <= 0xFF) {
								txBuffer[numTxBytes++] = (byte)GanglionCommand.Value1;
								txBuffer[numTxBytes++] = (byte) (aValue        & 0xFF);
								bytesAvailable -= 2;
							}
							else if (aValue <= 0xFFFF) {
								txBuffer[numTxBytes++] = (byte)GanglionCommand.Value2;
								txBuffer[numTxBytes++] = (byte) (aValue        & 0xFF);
								txBuffer[numTxBytes++] = (byte)((aValue >>  8) & 0xFF);
								bytesAvailable -= 3;
							}
							else if (aValue <= 0xFFFFFF) {
								txBuffer[numTxBytes++] = (byte)GanglionCommand.Value3;
								txBuffer[numTxBytes++] = (byte) (aValue        & 0xFF);
								txBuffer[numTxBytes++] = (byte)((aValue >>  8) & 0xFF);
								txBuffer[numTxBytes++] = (byte)((aValue >> 16) & 0xFF);
								bytesAvailable -= 4;
							}
							else {
								txBuffer[numTxBytes++] = (byte)GanglionCommand.Value4;
								txBuffer[numTxBytes++] = (byte) (aValue        & 0xFF);
								txBuffer[numTxBytes++] = (byte)((aValue >>  8) & 0xFF);
								txBuffer[numTxBytes++] = (byte)((aValue >> 16) & 0xFF);
								txBuffer[numTxBytes++] = (byte)((aValue >> 24) & 0xFF);
								bytesAvailable -= 5;
							}
							break;
						default:
							if (aPacket.aCmd == GanglionCommand.Error) {
								TxLog("ERROR: Sending GanglionCommand.Error.");
							}
							txBuffer[numTxBytes++] = (byte)aPacket.aCmd;
							bytesAvailable--;
							break;
					}
				}
				
				TxLog("Bytes to send: {0}", numTxBytes);
				if (numTxBytes > 0) {
					int bytesSent = m_serialPort.Write(txBuffer, numTxBytes);
					if (bytesSent >= 0) {
						TxLog("Bytes sent: {0}.\n", bytesSent);
					}
					else {
						TxLog("Write error: {0}.\n", m_serialPort.error);
					}
				}
			}
			catch (System.Threading.ThreadAbortException) {
				break;
			}
			catch (System.Exception e) {
				TxLog(e.ToString());
			}
		}
		TxLog("Closing.");
	}
	#endregion

	#endregion

	#region Debugging Code
#if DEBUG_TX || DEBUG_RX
	string m_dataPath;
#endif

#if DEBUG_TX
	const string kTxLogFileName = "/TxDebug.txt";
	System.IO.TextWriter m_txLog;
#endif

#if DEBUG_RX
	const string kRxLogFileName = "/RxDebug.txt";
	System.IO.TextWriter m_rxLog;
#endif

	[System.Diagnostics.Conditional("DEBUG_TX")]
	void TxLog(string format, params object[] args) {
#if DEBUG_TX
		string line = string.Format(format, args);
		if (m_txLog == null) {
			m_txLog = new System.IO.StreamWriter(m_dataPath + kTxLogFileName, false, 
			                                     System.Text.Encoding.UTF8);	
		}
		m_txLog.WriteLine(System.DateTime.Now.ToString() + ": " + line);
		m_txLog.Flush();
#endif
	}

	[System.Diagnostics.Conditional("DEBUG_RX")]
	void RxLog(string format, params object[] args) {
#if DEBUG_RX
		string line = string.Format(format, args);
		if (m_rxLog == null) {
			m_rxLog = new System.IO.StreamWriter(m_dataPath + kRxLogFileName, false, 
			                                     System.Text.Encoding.UTF8);	
		}
		m_rxLog.WriteLine(System.DateTime.Now.ToString() + ": " + line);
		m_rxLog.Flush();
#endif
	}

	[System.Diagnostics.Conditional("DEBUG_TX"), System.Diagnostics.Conditional("DEBUG_RX")]
	void OnDestroy() {
#if DEBUG_TX
		if (m_txLog != null) {
			m_txLog.Close();
		}
#endif

#if DEBUG_RX
		if (m_rxLog != null) {
			m_rxLog.Close();
		}
#endif
	}

	#endregion
#endif
}