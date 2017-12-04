using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class SegmentViewer : MonoBehaviour { 
	public MeshManager manager;
	public PrinterController printerController;
	public bool displaySegments = false;
	public bool displayQuadtrees = false;
	public bool displayOutlines = false;
	public bool showAllOutlines = false;
	
	int m_layer = -1;
	List<CartesianSegment> m_segments;
	Color[] m_colors = new Color[] {
		Color.red,
		Color.green,
		Color.blue,
		Color.yellow
	};
	Segmenter m_segmenter;
	QuadTree m_quad;
	List<QuadTree> m_branches;
	OutlineCreator m_outlineCreator;
	List<Outline> m_outlines;
	
	void Awake() {
		m_segments = new List<CartesianSegment>();
		m_segmenter = new Segmenter();
	}
	
	void OnGUI() {
		Rect r = new Rect(VisualFx.kToolbarMargin, VisualFx.kToolbarMargin, VisualFx.kTextButtonWidth, VisualFx.kTextButtonHeight);
		
		GUI.Label(r, "Layer " + m_layer);
		
		GUI.enabled = m_layer < manager.m_blob.height - 1;
		r.y += VisualFx.kTextButtonHeight + VisualFx.kToolbarMargin;
		if (GUI.Button(r, "Higher layer")) {
			ChangeLayer(1);	
		}
		
		GUI.enabled = m_layer > 0;
		r.y += VisualFx.kTextButtonHeight + VisualFx.kToolbarMargin;
		if (GUI.Button(r, "Lower layer")) {
			ChangeLayer(-1);	
		}
	}
	
	void ChangeLayer(int direction) {
		int nextLayer = Mathf.Clamp(m_layer + direction, 0, manager.m_blob.height - 1);
		if (nextLayer != m_layer) {
			m_layer = nextLayer;
			LoadSegments();
		}
	}
	
	void LoadSegments() {
		m_segments.Clear();
		foreach (VoxelRegion r in manager.m_blob.RegionEnumerator(m_layer, false)) {
			m_segments.AddRange(m_segmenter.GetSegments(r));
		}

		m_quad = new QuadTree(m_segments);
		m_branches = m_quad.FindAllBranches();
		m_outlineCreator = new OutlineCreator();
		m_outlines = m_outlineCreator.CreateOutlines(m_segments, null);
		Debug.Log("there were this many outlines created: " + m_outlines.Count);

		// QUESTION: Why repeat this?
		m_segments.Clear();
		foreach (VoxelRegion r in manager.m_blob.RegionEnumerator(m_layer, false)) {
			m_segments.AddRange(m_segmenter.GetSegments(r));
		}
	}
	
	void OnDrawGizmosSelected() {
		if(!Application.isPlaying) return;
		float scale = VoxelBlob.kVoxelSizeInMm;
		float layerY = m_layer;
		
		if (displaySegments) {
			foreach (CartesianSegment segment in m_segments) {
				Gizmos.color = m_colors[segment.material - 1];
				Vector3 p0 = new Vector3(segment.p0.x / scale, layerY, segment.p0.y / scale);
				Vector3 p1 = new Vector3(segment.p1.x / scale, layerY, segment.p1.y / scale);
				Gizmos.DrawLine(p0, p1);	
			}
		}
		
		if (displayQuadtrees) {
			int currentIndex = Mathf.RoundToInt(Mathf.PingPong(Time.time, m_branches.Count - 1));
			QuadTree q = m_branches[currentIndex];
			Gizmos.color = Color.white;
			float h = -10;
			
			Gizmos.DrawLine(new Vector3(q.lowerBound.x, h, q.lowerBound.y)/ scale, new Vector3(q.lowerBound.x, h, q.upperBound.y)/ scale);
			Gizmos.DrawLine(new Vector3(q.lowerBound.x, h, q.upperBound.y)/ scale, new Vector3(q.upperBound.x, h, q.upperBound.y)/ scale);
			Gizmos.DrawLine(new Vector3(q.upperBound.x, h, q.upperBound.y)/ scale, new Vector3(q.upperBound.x, h, q.lowerBound.y)/ scale);
			Gizmos.DrawLine(new Vector3(q.upperBound.x, h, q.lowerBound.y)/ scale, new Vector3(q.lowerBound.x, h, q.lowerBound.y)/ scale);
			
			Gizmos.color = Color.green;
			foreach(CartesianSegment sc in q.segments) {
				Gizmos.DrawLine(new Vector3(sc.p0.x, h, sc.p0.y)/ scale, new Vector3(sc.p1.x, h, sc.p1.y)/ scale);
			}
		}
		
		if (displayOutlines && m_outlines.Count > 0) {
			int currentIndex = Mathf.RoundToInt(Mathf.PingPong(Time.time, m_outlines.Count - 1));
			if (m_outlines.Count == 1) currentIndex = 0;
			float outlineY = -10;
			Gizmos.color = Color.white;
			foreach(CartesianSegment cs in m_outlines[currentIndex].segments) {
				Gizmos.DrawLine(new Vector3(cs.p0.x, outlineY, cs.p0.y) / scale, new Vector3(cs.p1.x, outlineY, cs.p1.y) / scale);
			}
		}
		if (showAllOutlines && m_outlines.Count > 0) {
			Gizmos.color = Color.yellow;
			foreach(Outline ol in m_outlines) {
				foreach(CartesianSegment cs in ol.segments) {
					Gizmos.DrawLine(new Vector3(cs.p0.x, -10, cs.p0.y) / scale, new Vector3(cs.p1.x, -10, cs.p1.y) / scale);
				}
			}
		}
		
	}
}
