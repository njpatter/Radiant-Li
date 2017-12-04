using System.Collections.Generic;

public class ConcurrentQueue<T> {
	Queue<T> m_queue = new Queue<T>();
	private readonly object m_lock = new object();
	
	public int Count {
		get {
			lock (m_lock) {
				return m_queue.Count;
			}
		}
	}

	public void Enqueue(T item) {
		lock (m_lock) {
			m_queue.Enqueue(item);
		}
	}
	
	public bool TryDequeue(out T item) {
		lock (m_lock) {
			if (m_queue.Count == 0) {
				item = default(T);
				return false;
			}
			
			item = m_queue.Dequeue();
			return true;
		}
	}
	
	public void Clear() {
		lock (m_lock) {
			m_queue.Clear();
		}
	}
}
