using UnityEngine;

/// <summary>
/// If you want your coroutine to yield for a certain number of frames, call
/// yield return new WaitFrames(x);
/// </summary>
class WaitFrames : IWaitCondition {
	int m_nextUpdateFrame;
	
	public WaitFrames(int frames) {
		m_nextUpdateFrame = Time.frameCount + frames;
	}
	
	public bool ShouldUpdate() {
		return Time.frameCount >= m_nextUpdateFrame;
	}
}
