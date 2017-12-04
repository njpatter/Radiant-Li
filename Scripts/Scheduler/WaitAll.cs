class WaitAll : IWaitCondition {
	IWaitCondition[] m_conditions;
	
	public WaitAll(params IWaitCondition[] conditions) {
		m_conditions = conditions;
	}
	
	public bool ShouldUpdate() {
		foreach (IWaitCondition cond in m_conditions) {
			if (!cond.ShouldUpdate())
				return false;
		}
		
		return true;
	}
}
