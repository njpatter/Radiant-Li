using UnityEngine;

/// <summary>
/// If you want your coroutine to yield for a certain amount of time,
/// call yield return new WaitSeconds(x). Similar to Unity's WaitForSeconds
/// </summary>
class WaitSeconds : IWaitCondition {
	float m_nextUpdateTime;
	
	public WaitSeconds(float sec) {
		m_nextUpdateTime = Time.realtimeSinceStartup + sec;
	}
	
	public bool ShouldUpdate() {
		return Time.realtimeSinceStartup >= m_nextUpdateTime;
	}
}
