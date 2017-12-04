#if UNITY_STANDALONE_WIN || UNITY_METRO
using UnityEngine;
using System.Collections;
using System.IO;
using System.IO.Ports;

/// <summary>
/// Window serial port controller. Essentially the same as the 
/// OS X version, but uses the native SerialPort class.
/// </summary>
public class WinSerialPort : System.Object, ISerialPort {
	const int kTimeout = 1;
	
	System.IO.Ports.SerialPort m_serial;

	public string error { get { return m_error; } }
	string m_error = "";

	/// <summary>
	/// Data we receive from the printer.
	/// </summary>
	byte[] m_receiveBuffer;

	/// <summary>
	/// Initializes a new instance of the <see cref="WinSerialPort"/> class.
	/// </summary>
	/// <param name="rxBufferSize">
	/// The size of the receive buffer.
	/// </param>
	public WinSerialPort(int rxBufferSize) {
		m_receiveBuffer = new byte[rxBufferSize];
	}

	/// <summary>
	/// Returns an array of available device ports based on the
	/// device path and device pattern.
	/// </summary>
	/// <returns>
	/// The ports.
	/// </returns>
	public string[] AvailablePorts() {
		string[] portPaths = System.IO.Ports.SerialPort.GetPortNames();
		return portPaths;
	}

	/// <summary>
	/// Opens the port at the provided path.
	/// </summary>
	/// <returns>
	/// The port.
	/// </returns>
	/// <param name='aPath'>
	/// If set to <c>true</c> a path.
	/// </param>
	public bool OpenPort(string aPath, int baudRate) {
		aPath = @"\\.\" + aPath;
		
		m_serial = new System.IO.Ports.SerialPort(aPath, baudRate, Parity.None, 8, StopBits.One);
		m_serial.ReadTimeout  = kTimeout;
		m_serial.WriteTimeout = SerialPort.InfiniteTimeout;
		m_serial.Encoding     = System.Text.Encoding.ASCII;
		m_serial.WriteBufferSize = 8192;
		//m_serial.ReadBufferSize  = 8192;
		m_serial.Open();
		
		bool success = m_serial.IsOpen;
		
		if (!success) {
			Text.Log(string.Format("Failed to open port at {0}.", aPath));
			m_serial = null;
		}
		
		return success;
	}

	/// <summary>
	/// Returns the open port's path, if any.
	/// </summary>
	/// <returns>The port path.</returns>
	public string OpenPortPath() {
		if (m_serial != null) {
			return m_serial.PortName;
		}
		return null;
	}

	/// <summary>
	/// Close an open port.
	/// </summary>
	public void Close() {
		if (m_serial != null) {
			m_serial.Close();
		}
	}

	/// <summary>
	/// Returns true if the port is open.
	/// </summary>
	/// <returns>
	/// The connected.
	/// </returns>
	public bool isConnected {
		get { return m_serial != null && m_serial.IsOpen; }
	}

	/// <summary>
	/// Read the specified number of bytes.
	/// </summary>
	/// <param name='numBytes'>
	/// Number bytes.
	/// </param>
	public int Read(int numBytes, out string result) {
		int numRead = -1;
		try {
			numRead = m_serial.Read(m_receiveBuffer, 0, numBytes);
			result = System.Text.ASCIIEncoding.ASCII.GetString(m_receiveBuffer, 0, Mathf.Min(numBytes, numRead));
		}
		catch (System.Exception e) { 
			m_error = e.Message;
			result = "";
		} 

		return result.Length;
	}
	
	/// <summary>
	/// Read the specified number of bytes into result.
	/// </summary>
	/// <param name="numBytes">Number bytes.</param>
	/// <param name="result">Result.</param>
	public int Read(int numBytes, ref byte[] result) {
		int numRead = -1;
		try {
			numRead = m_serial.Read(result, 0, numBytes);
		}
		catch (System.TimeoutException) {
			return 0;
		}
		catch (System.Exception e) { 
			m_error = e.Message;
		}

		return numRead;
	}

	/// <summary>
	/// Writes the message to the port.
	/// </summary>
	/// <param name='aMessage'>
	/// A message.
	/// </param>
	/// <returns>The number of bytes written</returns>
	public int Write(string aMessage) {
		int numWritten = -1;
		try {
			m_serial.Write(aMessage);
			numWritten = aMessage.Length;
		}
		catch (System.Exception e) { 
			m_error = e.Message;
		}

		return numWritten;
	}
	
	/// <summary>
	/// Writes length bytes from the bytes buffer.
	/// </summary>
	/// <returns>The bytes.</returns>
	/// <param name="bytes">Bytes to write.</param>
	/// <param name="length">Number to write.</param>
	public int Write(byte[] bytes, int length) {
		int numWritten = -1;
		try {
			m_serial.Write(bytes, 0, length);
			numWritten = length;
		}
		catch (System.Exception e) { 
			m_error = e.Message;
		}

		return numWritten;
	}

	/// <summary>
	/// Raises the DTR.
	/// </summary>
	public void RaiseDtr() { m_serial.DtrEnable = true; }

	/// <summary>
	/// Clears the DTR.
	/// </summary>
	public void ClearDtr() { m_serial.DtrEnable = false; }

	/// <summary>
	/// Flushes the IO buffers.
	/// </summary>
	public void Flush() { m_serial.BaseStream.Flush(); }
}

#endif