#if UNITY_STANDALONE_OSX
using UnityEngine;
using System.Collections;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;

/// <summary>
/// The OS X serial port wrapper.
/// </summary>
public class OsxSerialPort : System.Object, ISerialPort {
	#region Constants	
	/// <summary>
	/// The path, including trailing slash, where device files
	/// are located.
	/// </summary>
	const string kDevicePath = "/dev/";
	
	/// <summary>
	/// The pattern used to locate valid devices.
	/// </summary>
	const string kDevicePattern = "cu.usbserial*";
	
	/// <summary>
	/// Indicates a closed port.
	/// </summary>
	const int kPortClosed = -1;
	#endregion

	#region Bundle wrappers
	/// <summary>
	/// Opens the target path as a serial port.
	/// </summary>
	/// <param name='aFileLocation'>
	/// A file location.
	/// </param>
	[DllImport("SerialPort6")]
	private static extern int open_port(string aFileLocation, int baud_rate);

	/// <summary>
	/// Closes the port with the specified id.
	/// </summary>
	/// <param name='portId'>
	/// Port identifier.
	/// </param>
	[DllImport("SerialPort6")]
	private static extern void close_port(int portId);

	/// <summary>
	/// Writes bufferSize bytes in buffer to the specified file descriptor.
	/// </summary>
	/// <param name="fd">The file descriptor.</param>
	/// <param name="buffer">The buffer of bytes.</param>
	/// <param name="bufferSize">The number of bytes.</param>
	[DllImport("SerialPort6")]
	private static extern int write_bytes(int fd, byte[] buffer, int bufferSize);

	/// <summary>
	/// Reads bufferSize bytes from the specified port into the providied buffer.
	/// </summary>
	/// <param name='portId'>
	/// Port identifier.
	/// </param>
	/// <param name='buffer'>
	/// Buffer.
	/// </param>
	/// <param name='bufferSize'>
	/// Buffer size.
	/// </param>
	[DllImport("SerialPort6")]
	private static extern int read_bytes(int portId, byte[] buffer, int bufferSize);

	[DllImport("SerialPort6")]
	private static extern int read_bytes_timeout(int portId, byte[] buffer, int bufferSize, int timeout);

	/// <summary>
	/// Raises DTR for the specified file descriptor.
	/// </summary>
	/// <param name="fd">The file descriptor.</param>
	[DllImport("SerialPort6")]
	private static extern void raise_dtr(int fd);

	/// <summary>
	/// Clears DTR for the specified file descriptor.
	/// </summary>
	/// <param name="fd">The file descriptor.</param>
	[DllImport("SerialPort6")]
	private static extern void clear_dtr(int fd);
	
	/// <summary>
	/// Flushes the input and output buffers.
	/// </summary>
	/// <param name='fd'>
	/// The file descriptor.
	/// </param>
	[DllImport("SerialPort6")]
	private static extern void flush(int fd);

	[DllImport("SerialPort6")]
	private static extern int error_code();
	#endregion

	public string error { get { return m_error; } }
	string m_error = "";

	/// <summary>
	/// The port we're connected to. < 0 indicates unconnected.
	/// </summary>
	int m_portId = kPortClosed;
	
	/// <summary>
	/// A cached copy of the file path opened.
	/// </summary>
	string m_openedPortPath;

	/// <summary>
	/// Data we receive from the printer.
	/// </summary>
	byte[] m_receiveBuffer;

	/// <summary>
	/// Initializes a new instance of the <see cref="OsxSerialPort"/> class.
	/// </summary>
	/// <param name="rxBufferSize">
	/// The size of the receive buffer.
	/// </param>
	public OsxSerialPort(int rxBufferSize) {
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
		string[] portPaths = Directory.GetFiles(kDevicePath, kDevicePattern);
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
	/// <param name="baudRate">
	/// The baud rate to open.
	/// </param>
	public bool OpenPort(string aPath, int baudRate) {
		m_portId = open_port(aPath, baudRate);

		bool success = m_portId > kPortClosed;
		if (success) {
			m_openedPortPath = aPath;
		}
		
		return success;
	}

	/// <summary>
	/// Returns the open port's path, if any.
	/// </summary>
	/// <returns>The port path.</returns>
	public string OpenPortPath() {
		return m_openedPortPath;
	}

	/// <summary>
	/// Close an open port.
	/// </summary>
	public void Close() {
		if (m_portId <= kPortClosed) return;

		close_port(m_portId); 
		m_portId = kPortClosed;
	}
	
	/// <summary>
	/// Returns true if the port is open.
	/// </summary>
	/// <returns>
	/// The connected.
	/// </returns>
	public bool isConnected {
		get { return m_portId > kPortClosed; }
	}

	/// <summary>
	/// Read the specified number of bytes.
	/// </summary>
	/// <param name='numBytes'>
	/// Number bytes.
	/// </param>
	public int Read(int numBytes, out string result) {
		int numRead = read_bytes(m_portId, m_receiveBuffer, numBytes);
		if (numRead > 0) {
			int i;
			for (i = 0; i < numRead && m_receiveBuffer[i] < 32; ++i);
			result = System.Text.ASCIIEncoding.ASCII.GetString(m_receiveBuffer, i, Mathf.Min(numBytes, numRead - i));
		}
		else {
			result = "";
			m_error = error_code().ToString();
			return -1;
		}

		return numRead;
	}

	/// <summary>
	/// Read the specified number of bytes into result.
	/// </summary>
	/// <param name="numBytes">Number bytes.</param>
	/// <param name="result">Result.</param>
	public int Read(int numBytes, ref byte[] result) {
		int numRead = read_bytes(m_portId, result, numBytes);
		//int numRead = read_bytes_timeout(m_portId, result, numBytes, 10);
		if (numRead < 0) {
			result[0] = (byte)'\0';
			m_error = error_code().ToString();
			return -1;
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
		int numWritten = write_bytes(m_portId, 
			System.Text.ASCIIEncoding.ASCII.GetBytes(aMessage), 
			aMessage.Length);
		if (numWritten < 0) {
			m_error = error_code().ToString();
			return -1;
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
		int numWritten = write_bytes(m_portId, bytes, length);
		if (numWritten < 0) {
			m_error = error_code().ToString();
			return -1;
		}
		return numWritten;
	}

	/// <summary>
	/// Raises the DTR.
	/// </summary>
	public void RaiseDtr() {
		//Contract.Assert(m_portId > kPortClosed, @"RaiseDTR called with closed port.");
		raise_dtr(m_portId); 
	}

	/// <summary>
	/// Clears the DTR.
	/// </summary>
	public void ClearDtr() {
		//Contract.Assert(m_portId > kPortClosed, @"ClearDTR called with closed port.");
		clear_dtr(m_portId); 
	}
	
	/// <summary>
	/// Flushes the IO buffers.
	/// </summary>
	public void Flush() {
		//Contract.Assert(m_portId > kPortClosed, @"Flush called with closed port.");
		flush(m_portId);	
	}
}

#endif
