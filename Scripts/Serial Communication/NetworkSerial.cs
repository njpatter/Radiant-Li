/*
 * Needs to have significant chunks changed if we're going to use it.

using UnityEngine;
using System;
using System.Collections;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

public class NetworkSerial : MonoBehaviour, ICommandConsumer {
	
	IPEndPoint m_printerIp = null;
	TcpClient m_printerClient = null;
	bool m_foundPrinter = false;
	Thread m_connectionThread = null;
	byte[] m_currentReceiveLine;
	const float kReadLineTimeoutInSec = 10.0f;
	const int kPort = 13000;

	ConcurrentQueue<byte[]> m_receiveQueue = new ConcurrentQueue<byte[]>();
	ConcurrentQueue<byte[]> m_sendQueue = new ConcurrentQueue<byte[]>();
	
	IEnumerator OnPrinterFound() {
		yield return new WaitUntil(() => m_printerIp != null );
		Text.Log(m_printerIp.ToString());
		m_printerClient = new TcpClient();
		m_printerClient.Connect(m_printerIp.Address, kPort);
		m_foundPrinter = true;
			
		m_connectionThread = new Thread(DoConnection);
		m_connectionThread.Start();
	}
	
	void OnDestroy() {
		if (m_connectionThread != null)
			m_connectionThread.Abort();
		m_connectionThread = null;
	}
	
	void OnEnable() {
		ScanForPrinter();
	}
	
	void OnDisable() {
		m_foundPrinter = false;
		m_printerIp = null;
		if (m_connectionThread != null) {
			m_connectionThread.Abort();
			m_connectionThread = null;
		}
		
		m_receiveQueue.Clear();
		m_sendQueue.Clear();
	}
	
	void ScanForPrinter() {
		Scheduler.StartCoroutine(OnPrinterFound());
		UdpClient udp = new UdpClient(new IPEndPoint(IPAddress.Any, kPort));
		udp.BeginReceive(UdpReceive, udp);
	}
	
	void UdpReceive(IAsyncResult ar) {
		UdpClient udp = (UdpClient)ar.AsyncState;
		IPEndPoint ip = null;
		byte[] msg = udp.EndReceive(ar, ref ip);
		if (Encoding.UTF8.GetString(msg) == "Radiant Printer") {
			m_printerIp = ip;
			udp.Close();
		}
		else {
			udp.BeginReceive(UdpReceive, udp);
		}
	}
	
	void DoConnection() {
		NetworkStream stream = m_printerClient.GetStream();
		byte[] buffer = new byte[256];
		while (true) {
			if (stream.DataAvailable) {
				try {
					int numBytes = stream.Read(buffer, 0, 256);
					byte[] msg = new byte[numBytes];
					Array.ConstrainedCopy(buffer, 0, msg, 0, numBytes);
					m_receiveQueue.Enqueue(msg);
				}
				catch (Exception) {
					m_foundPrinter = false;
					m_printerClient.Close();
					m_printerIp = null;
					return;
				}
			}
			
			byte[] send = null;
			if (m_sendQueue.TryDequeue(out send)) {
				try {
					stream.Write(send, 0, send.Length);
				}
				catch (Exception) {
					m_foundPrinter = false;
					m_printerClient.Close();
					return;
				}
			}
			
			Thread.Sleep(0);
		}
	}
	
	public bool providesFeedback { get { return true; } }
	
	public IEnumerator NextLine() { 
		float startTime = Time.realtimeSinceStartup;
		yield return new WaitUntil(() => m_receiveQueue.Count > 0 || 
			Time.realtimeSinceStartup - startTime > kReadLineTimeoutInSec);
		
		m_receiveQueue.TryDequeue(out m_currentReceiveLine);
	}
	
	public string readLine { 
		get {
			if (m_currentReceiveLine == null)
				return String.Empty;
			else
				return Encoding.UTF8.GetString(m_currentReceiveLine); 
		} 
	}

	public void ClearRxBuffer() {
		Text.Error("Clearing the RX for Network Serial isn't set up yet");
	}
	
	public void SendPacket(GanglionCommand aCommand) {
		SendPacket(new TxPacket(aCommand));
	}
	
	public void SendPacket(GanglionCommand aCommand, int anArg) {
		SendPacket(new TxPacket(aCommand, anArg));
	}
	
	public void SendPacket(GanglionCommand aCommand, DataReceived onReceive) {
		if (!m_foundPrinter) Text.Warning("Printer not found yet!");
		


		m_sendQueue.Enqueue(Encoding.UTF8.GetBytes(" "));
	}
	
	public void SendPacket(TxPacket datum) {


		if (aCommand == GanglionCommand.Value) {
			m_sendQueue.Enqueue(Encoding.UTF8.GetBytes( anArg.ToString()));
		}
		else {
			m_sendQueue.Enqueue(Encoding.UTF8.GetBytes(Ganglion.kCmdToStr[aCommand]));
		}
	}
}
*/
