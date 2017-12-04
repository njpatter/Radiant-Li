using UnityEngine;
using System.Collections;
using System.IO;

/// <summary>
/// Logs all data being sent.
/// </summary>
public class SerialLogger : MonoBehaviour, ICommandConsumer {
	/// <summary>
	/// The default log file.
	/// </summary>
	const string kLogFileName = "/RCE.txt";
	
	/// <summary>
	/// The handle for the log file.
	/// </summary>
	TextWriter m_logFile;
	
	/// <summary>
	/// Used to pretty-print the file.
	/// </summary>
	int m_charsWritten;
	
	/// <summary>
	/// Creates the log file.
	/// </summary>
	void Awake() {
		m_logFile = new StreamWriter(Application.persistentDataPath + kLogFileName, 
		                             false, System.Text.Encoding.UTF8);
	}
	
	/// <summary>
	/// Closes the log file.
	/// </summary> 
	void OnDestroy() {
		m_logFile.Flush();
		m_logFile.Close();
	}
	
	/// <summary>
	/// Gets a value indicating this <see cref="SerialController"/> doesn't provides feedback.
	/// </summary>
	/// <value>
	/// <c>false</c>.
	/// </value>
	public bool providesFeedback { get { return false; } }

	/// <summary>
	/// Returns the number of packets still queued.
	/// </summary>
	/// <value>The number of unsent packets.</value>
	public int packetsRemaining { get { return 0; } }

	/// <summary>
	/// Returns the last temperature of the
	/// requested heater.
	/// </summary>
	/// <returns>The temperature of heater #index.</returns>
	/// <param name="index">The heater index, 0-based.</param>
	public int HeaterTemp(int index) { return 0; }
	
	/// <summary>
	/// Returns the bytes available to receive.
	/// </summary>
	/// <value>The rx bytes available.</value>
	public int rxBytesAvailable { get { return 0; } }
	
	/// <summary>
	/// Returns the number of motor queue slots open.
	/// </summary>
	/// <value>The motor queue slots available.</value>
	public int motorQueueAvailable { get { return 0; } }
	
	
	/// <summary>
	/// Returns whether or not the consumer is seeking.
	/// </summary>
	/// <value><c>true</c> if is seeking; otherwise, <c>false</c>.</value>
	public bool isSeeking { get { return false; } }

	public void BeginSendingPackets() { }
	public void EndSendingPackets() { }

	/// <summary>
	/// Queues a packet to send.
	/// </summary>
	/// <param name='aCommand'>
	/// A command.
	/// </param>
	public void SendPacket(GanglionCommand aCommand) {
		SendPacket(new TxPacket(aCommand));
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
	/// Queues a packet to send.
	/// </summary>
	/// <param name="datum">
	/// A pre-created packet of command, arg, and callback.
	/// </param>
	public void SendPacket(TxPacket datum) {
		GanglionCommand aCommand = datum.aCmd;
		int anArg = datum.anArgument;

		if (aCommand == GanglionCommand.Value) {
			m_logFile.Write(anArg);
			m_charsWritten += anArg.ToString().Length + 1;
		}
		else {
			m_logFile.Write(Ganglion.kCmdToStr[aCommand]);
			m_charsWritten += aCommand.ToString().Length + 1;
		}
		
		if (   aCommand == GanglionCommand.Step 
		    || aCommand == GanglionCommand.Heat
		    || aCommand == GanglionCommand.StepRate
		    || aCommand == GanglionCommand.Stop
		    || aCommand == GanglionCommand.StepRateStep
		    || m_charsWritten > 60) 
		{
			m_logFile.WriteLine();
			m_charsWritten = 0;
		}
		else m_logFile.Write(" ");
	}
}
