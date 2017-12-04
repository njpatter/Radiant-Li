using UnityEngine;
using System.Collections;

/// <summary>
/// Helper class to encapsulate commands, arguments, and
/// callbacks for sent commands.
/// </summary>
public class TxPacket {
	public GanglionCommand aCmd;
	public int anArgument;
		
	public TxPacket(GanglionCommand aCommand) : this(aCommand, 0) { }

	public TxPacket(GanglionCommand aCommand, int anArg) {
		aCmd = aCommand;
		anArgument = anArg;
	}
}