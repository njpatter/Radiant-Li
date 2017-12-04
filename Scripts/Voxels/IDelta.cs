using UnityEngine;
using System.Collections.Generic;

public delegate void DeltaDoneDelegate();

public interface IDelta {
	/// <summary>
	/// Called when this is done during an undo.
	/// </summary>
	void UndoAction(MeshManager manager, DeltaDoneDelegate onDone);
	/// <summary>
	/// Called when this is done during a redo.
	/// </summary>
	void RedoAction(MeshManager manager, DeltaDoneDelegate onDone);
	
	bool CanUndo();
	bool CanRedo();

	bool Stop();
	
	bool Valid { get; }
}
