/*
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class FTDIConnection : System.Object, ISerialPort {
	FtdiInterface m_ftdi;
	byte[] m_readBuffer;
	int    m_readIndex;
	
	public FTDIConnection() {
		m_ftdi = new FtdiInterface();
	}
	
	public string[] AvailablePorts() {
		FtdiInterface.DeviceInfo[] devices = FtdiInterface.GetDeviceList();
		string[] descriptions = new string[devices.Length];
		for (int i = 0; i < devices.Length; ++i) {
			descriptions[i] = devices[i].description;
		}
		return descriptions;
	}
	
	public bool OpenPort(string aPort, int baudRate) {
		m_ftdi.OpenByDescription(aPort);
		m_ftdi.SetBaudRate((uint)baudRate);
		m_ftdi.SetLatency(16);
		
		m_readIndex = 0;
		m_readBuffer = null;
		return true;
	}
		
	public void SetBlocking() {
		Text.Error(@"Not implemented.");
	}

	public void Close() {
		m_ftdi.Close();	
	}
	
	public bool isConnected{ 
		get { return m_ftdi == null ? false : m_ftdi.isOpen; } 
	}
	
	public void FillReadBufferIfNeeded() {
		if (m_readBuffer == null) {
			m_readBuffer = m_ftdi.TryRead();
			m_readIndex = 0;
		}
	}
	
	public int NextByte() {
		if (!isConnected) return -1;
		
		if (m_readBuffer == null || m_readIndex >= m_readBuffer.Length) {
			m_readBuffer = null;
			FillReadBufferIfNeeded();
		}
		// Nothing to read?
		if (m_readBuffer == null) return -1;
		
		// Something to read.
		return m_readBuffer[m_readIndex++];
	}
	
	public bool ReadByte(out byte theReadByte) {
		int next = NextByte();
		theReadByte = (byte)next;
		return next >= 0;
	}
	
	public bool ReadByte(out byte theReadByte, int timeout) {
		return ReadByte(out theReadByte);
		
		// If we can use the FTDI driver on both Windows & Mac,
		// then we should get rid of this method. Otherwise,
		// we'll need to sleep here for up to timeout ms.
	}
	
	public int Read(int numBytes, out string result) {
		System.Text.StringBuilder buffer = new System.Text.StringBuilder();
		
		while (numBytes-- > 0) {
			byte datum;
			bool didRead = ReadByte(out datum);
			if (!didRead) {
				result = buffer.ToString();
				return buffer.Length;
			}
			buffer.Append(datum);
		}
		result = buffer.ToString();
		return result.Length;
	}
	
	public int Write(string aMessage) {
		return (int)m_ftdi.Write(System.Text.UTF8Encoding.UTF8.GetBytes(aMessage));
	}
	
	public int Write(byte[] bytes, int length) {
		return (int)m_ftdi.Write(bytes, length);	
	}
	
	public void RaiseDtr() {
		m_ftdi.SetDtr(true);	
	}
	
	public void ClearDtr() {
		m_ftdi.SetDtr(false);
	}
	
	public void Flush() {
		// Not supported.
	}
}
*/
