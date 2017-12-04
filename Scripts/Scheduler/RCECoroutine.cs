using UnityEngine;
using System.Collections;

/// <summary>
/// Contains the information for each coroutine. Didn't use coroutine due to it being used by Unity
/// </summary>
public class RCECoroutine {
	public IEnumerator method { get { return m_method; }}
	IEnumerator m_method;
	/// <summary>
	/// How long did we take last frame.
	/// </summary>
	public float lastElapsedTime;
	public float allowedTime;
	float m_startTime;
	/// <summary>
	/// The object that started us; optional.
	/// </summary>
	public System.Object originator;
	
	/// <summary>
	/// A shifting weighted average of how much the coroutine goes over its alotted time. Penalizes them by this much.
	/// </summary>
	float m_cheatTime = 0f;
	/// <summary>
	/// We didn't take all of our allotted time last frame.
	/// </summary>
	public bool underTime = false;
	
	public bool done = false;
	
	IWaitCondition m_waitCondition = null;
	
	public bool updatedThisFrame = false;
	
	public RCECoroutine(IEnumerator method, System.Object originator) {
		m_method = method;
		this.originator = originator;
		UpdateNextRunStatus(method.Current);
	}
	
	/// <summary>
	/// Update this coroutine. Returns false when complete.
	/// </summary>
	public bool Update() {
		if (done) return false;
		if (!ShouldUpdate()) return true;
		
		m_startTime = Time.realtimeSinceStartup;
		bool res = m_method.MoveNext();
		lastElapsedTime = Time.realtimeSinceStartup - m_startTime;
		underTime = (lastElapsedTime < allowedTime - m_cheatTime);
		m_cheatTime = Mathf.Max(0f, m_cheatTime * 0.9f + (lastElapsedTime - allowedTime) * 0.1f);
		
		UpdateNextRunStatus(m_method.Current);
		
		done = !res;
		return res;
	}
	
	public bool ShouldYield() {
		return Time.realtimeSinceStartup - m_startTime >= allowedTime - m_cheatTime;
	}
	
	public bool ShouldUpdate() {
		if (m_waitCondition == null) return true;
		
		return m_waitCondition.ShouldUpdate();
	}
	
	/// <summary>
	/// Figures out when the next time we should run.
	/// </summary>
	/// <param name='status'>
	/// The object returned from a yield
	/// </param>
	/// <exception cref='System.Exception'>
	/// Will throw an exception if we get a yield object we don't recognize
	/// </exception>
	void UpdateNextRunStatus(object status) {
		//potential TODO: Support a version of WaitForFixedUpdate and WaitForEndOfFrame
		if (status == null) {
			m_waitCondition = new WaitFrames(1);
		}
		else if (typeof(IWaitCondition).IsAssignableFrom(status.GetType())) {
			m_waitCondition = (IWaitCondition)status;
		}
		else if(status.GetType() == typeof(RCECoroutine)) {
			m_waitCondition = new WaitCoroutine((RCECoroutine)status);
		}
		else if (status.GetType() == typeof(WaitForSeconds)) {
			throw new System.Exception("Don't use WaitForSeconds. Use WaitSeconds instead.");
		}
		else {
			throw new System.Exception("Unhandled yield type to RCECoroutine: " + status.ToString());
		}
	}

	public override string ToString ()
	{
		return string.Format ("[RCECoroutine: method={0}, originator={1}]", method, originator);
	}
}

