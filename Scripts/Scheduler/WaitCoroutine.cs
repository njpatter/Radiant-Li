public class WaitCoroutine : IWaitCondition {
	RCECoroutine m_coroutine;
	
	public WaitCoroutine(RCECoroutine co) {
		m_coroutine = co;
	}
	
	public bool ShouldUpdate() {
		return m_coroutine.done;
	}
}

