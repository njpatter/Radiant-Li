
public interface ISerialPort {
	string[] AvailablePorts();
	string error { get; }

	bool OpenPort(string aPort, int baudRate);
	string OpenPortPath();
	void Close();
	bool isConnected{ get; }

	int Read(int numBytes, out string result);
	int Read(int numBytes, ref byte[] result);
	
	int Write(string aMessage);
	int Write(byte[] bytes, int length);

	void RaiseDtr();
	void ClearDtr();
	
	void Flush();
}
