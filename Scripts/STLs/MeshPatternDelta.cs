using UnityEngine;
using System.Collections;

public class MeshPatternDelta : IDelta {
	public BlobDelta blobDelta;
	
	MeshPatternConverter.Data m_data;
	MeshPatternConverter m_converter;
	
	DeltaDoneDelegate m_currentCallback;
	
	public MeshPatternDelta(MeshPatternConverter converter, MeshPatternConverter.Data data, int chunkSize) {
		blobDelta = new BlobDelta(chunkSize);
		m_converter = converter;
		m_data = data;
	}
	
	public void UndoAction(MeshManager manager, DeltaDoneDelegate onDone) {
		blobDelta.UndoAction(manager, onDone);
		
		if (m_converter != null) {
			m_converter.shouldAbort = true;
			m_converter = null;
		}
	}
	
	public void RedoAction(MeshManager manager, DeltaDoneDelegate onDone) {
		if (m_data == null) {
			blobDelta.RedoAction(manager, onDone);
			return;
		}
		
		GameObject go = new GameObject("MeshPatternConverter");
		m_converter = go.AddComponent<MeshPatternConverter>();
		
		m_converter.Init(manager, m_data, this);
		
		Scheduler.StartCoroutine(MeshPattern.CreateMeshObject(go, m_data.faces, m_data.meshMat, LayerMask.NameToLayer("Voxel"), 
			(m_data.material != MeshManager.kVoxelSubtract)));
		
		m_currentCallback = onDone;
		blobDelta.RedoAction(manager, RestartConverter);
	}
	
	public bool CanUndo() { return true; }
	public bool CanRedo() { return true; }

	public bool Stop() {
		if (m_converter != null) {
			m_converter.shouldAbort = true;
			m_converter = null;
			return true;
		}
		return false;
	}
	
	void RestartConverter() {
		Scheduler.StartCoroutine(m_converter.Convert());
	}
	
	public void MarkConversionDone() {
		m_converter = null;
		m_data = null;
		if (m_currentCallback != null) m_currentCallback();
	}
	
	public bool Valid { get { return true; } }
}
