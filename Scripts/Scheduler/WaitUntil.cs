/// <summary>
/// Wait until the passed delegate returns true.
/// </summary>
public class WaitUntil : IWaitCondition {
	public delegate bool Cond();
	Cond m_condition;
	
	public WaitUntil(Cond condition) {
		m_condition = condition;
	}
	
	public bool ShouldUpdate() {
		return m_condition();
	}
}
