using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class Scheduler : MonoBehaviour {
	/// <summary>
	/// The singleton instance. Not created until first accessed.
	/// </summary>
	static Scheduler sm_instance = null;
	
	public static Scheduler instance {
		get {
			if (sm_instance == null) {
				GameObject go = new GameObject("Scheduler");
				sm_instance = go.AddComponent<Scheduler>();
			}
			return sm_instance;
		}
	}
	
	List<RCECoroutine> m_coroutines = new List<RCECoroutine>();
	float m_timeSlice;
	
	#region State Variables
	//current state variables used during update, rather than passing everything by ref to helper functions
	
	/// <summary>
	/// Maintain a list of new coroutines so we don't mess up our iterations during update.
	/// </summary>
	List<RCECoroutine> m_newCoroutines = new List<RCECoroutine>();
	/// <summary>
	/// The dead fibers. Updated and cleared every frame.
	/// </summary>
	List<RCECoroutine> m_deadCoroutines = new List<RCECoroutine>();
	
	/// <summary>
	/// The coroutine we are currently processing. Needed so that coroutine can ask us if it should yield
	/// </summary>
	RCECoroutine m_currentCoroutine = null;
	/// <summary>
	/// How much time we have left to process coroutines this frame.
	/// </summary>
	float m_timeRemaining;
	/// <summary>
	/// How many coroutines we have to process this frame.
	/// </summary>
	int m_coroutinesRemaining;
	#endregion
	
	/// <summary>
	/// The target time we aim for each frame. Simply 1 / targetFps
	/// </summary>
	const float kTargetTime = 1f / 30f;
	//if our time slice goes above this threshold start giving back time until we get up to our target time
	const float kUpperSliceThreshold = 0.01f;
	//if our time slice goes below that start stealing time from our target time
	const float kLowerSliceThreshold = 0.002f;
	//if we are consistently missing our target, adjust down so we don't starve the coroutines
	float m_actualTargetTime = kTargetTime;
	float m_lastUpdateTime = 0f;

	void OnDestroy() {
		sm_instance = null;
	}

	public static new RCECoroutine StartCoroutine(string methodName) {
		//if this becomes necessary it can be implemented; it's here to hide Unity's version
		Text.Error("Unimplemented");
		return null;
	}
	
	public static new RCECoroutine StartCoroutine(string methodName, object source) {
		Text.Error("Unimplemented.");
		return null;
	}

	public static RCECoroutine StartCoroutine(IEnumerator method, object source) {
		return instance.StartCoroutineInstance(method, source);
	}
	
	/// <summary>
	/// Starts a coroutine. Used exactly like Unity's default StartCoroutine
	/// </summary>
	/// <returns>
	/// The coroutine.
	/// </returns>
	/// <param name='method'>
	/// The method.
	/// </param>
	public static new RCECoroutine StartCoroutine(IEnumerator method) {
		return instance.StartCoroutineInstance(method, null);
	}

	/// <summary>
	/// The non-static version of StartCoroutine.
	/// </summary>
	RCECoroutine StartCoroutineInstance(IEnumerator method, System.Object source) {
		if (method == null) {
			return null;
		}
		RCECoroutine fib = new RCECoroutine(method, source);
		m_newCoroutines.Add(fib);
		return fib;
	}
	
	public static bool ShouldYield() {
		return instance.ShouldYieldInstance();
	}

	public static void StopCoroutines(System.Object source) {
		for (int i = 0; i < instance.m_coroutines.Count; i++) {
			RCECoroutine c = instance.m_coroutines[i];
			if (c.originator == source) {
				c.done = true;
			}
		}

		for (int i = 0; i < instance.m_newCoroutines.Count; i++) {
			RCECoroutine c = instance.m_newCoroutines[i];
			if (c.originator == source) {
				c.done = true;
			}
		}
	}
	
	/// <summary>
	/// The non-static version of ShouldYield.
	/// </summary>
	bool ShouldYieldInstance() {
		// not our coroutine (yet), just tell them to yield
		if (m_currentCoroutine == null) {
			return true;
		}
		else {
			return m_currentCoroutine.ShouldYield();
		}
	}
	
	public static void DumpAllowedTime() {
		Text.Log("Allowed Time: " + instance.m_currentCoroutine.allowedTime);
	}
	
	void UpdateCoroutine(RCECoroutine co, float timeAllowed) {
		m_currentCoroutine = co;
		co.allowedTime = timeAllowed;

		if (!co.Update()) {
			m_deadCoroutines.Add(co);
		}
		
		--m_coroutinesRemaining;
		m_timeRemaining -= co.lastElapsedTime;
	}
	
	void Update() {
		//calculate our time slice by creating a weighted average of the estimated time we have left in the frames
		m_timeSlice = m_timeSlice * 0.8f +  Mathf.Max(m_actualTargetTime - (Time.realtimeSinceStartup - m_lastUpdateTime), 0f) * 0.2f;
		
		//every time our time slice is low increase our target time a bit; if we're high enough try to give back time
		if (m_timeSlice < kLowerSliceThreshold) {
			m_actualTargetTime *= 1.01f;
		}
		else if (m_timeSlice > kUpperSliceThreshold) {
			m_actualTargetTime = Mathf.Max(kTargetTime, m_actualTargetTime * 0.99f);
		}

		for (int i = 0; i < m_newCoroutines.Count; i++) {
			m_coroutines.Add(m_newCoroutines[i]);
		}
		m_newCoroutines.Clear();
		
		m_timeRemaining = m_timeSlice;
		m_coroutinesRemaining = m_coroutines.Count;
		
		if (m_timeRemaining > 0f) {
			//pass to weed out the paused and underutilizing threads
			for (int i = 0; i < m_coroutines.Count; i++) {
				RCECoroutine co = m_coroutines[i];

				if (!co.ShouldUpdate()) {
					--m_coroutinesRemaining;
					co.updatedThisFrame = true;
				}
				else if (co.underTime && co.lastElapsedTime < m_timeRemaining / m_coroutinesRemaining) {
					UpdateCoroutine(co, co.lastElapsedTime);
					co.updatedThisFrame = true;
				}
				else
					co.updatedThisFrame = false;
			}
			
			//do the rest
			//TODO: Right now everyone gets a more-or-less equal share; In the future certain coroutines
			//may have a higher priority and thus need more time
			for (int i = 0; i < m_coroutines.Count; i++) {
				RCECoroutine co = m_coroutines[i];
			
				if (!co.updatedThisFrame && co.ShouldUpdate()) {
					if (m_coroutinesRemaining == 0) throw new System.Exception("Div zero");
					UpdateCoroutine(co, m_timeRemaining / m_coroutinesRemaining);
					co.updatedThisFrame = true;
				}
			}
		}
		else {
			//everyone gets zero time, which means they will run one "loop" and stop
			//potential TODO: starve the coroutines entirely if we drop below a minimum threshold
			for (int i = 0; i < m_coroutines.Count; i++) {
				RCECoroutine co = m_coroutines[i];
			
				if (!co.updatedThisFrame && co.ShouldUpdate()) {
					UpdateCoroutine(co, 0f);
					co.updatedThisFrame = true;
				}
			}
		}
		
		//remove the dead
		for (int i = 0; i < m_deadCoroutines.Count; i++) {
			m_coroutines.Remove(m_deadCoroutines[i]);
		}
		m_deadCoroutines.Clear();
		
		m_currentCoroutine = null;
		m_lastUpdateTime = Time.realtimeSinceStartup;
	}
}
