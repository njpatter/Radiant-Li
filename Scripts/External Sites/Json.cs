using UnityEngine;
using System.Collections.Generic;

public class Json : System.Object {
	Dictionary<string, string> m_payload;
	
	public Json() {
		m_payload = new Dictionary<string, string>();	
	}
	
	public void Add(string key, string aValue) {
		m_payload[Quote(key)] = Quote(aValue);
	}
	
	public void Add(string key, int aValue) {
		m_payload[Quote(key)] = aValue.ToString();
	}
	
	public void Add(string key, float aValue) {
		m_payload[Quote(key)] = aValue.ToString();
	}
	
	public void Add(string key, string[] values) {
		string[] quoted = new string[values.Length];
		for (int i = 0; i < values.Length; ++i) {
			quoted[i] = Quote(values[i]);
		}
		
		m_payload[Quote(key)] = string.Format("[ {0} ]",
			string.Join(",", quoted));
	}
	
	public override string ToString() {
		System.Text.StringBuilder buffer = new System.Text.StringBuilder("{");
		foreach (KeyValuePair<string, string> aPair in m_payload) {
			buffer.AppendFormat("{0}: {1},", aPair.Key, aPair.Value);
		}
		buffer.Remove(buffer.Length - 1, 1);
		buffer.Append("}");
		
		return buffer.ToString();
	}
	
	string Quote(string aValue) {
		return string.Format("\"{0}\"", aValue);	
	}
}
