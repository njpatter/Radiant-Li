using System;
using System.Reflection;
using System.Runtime.Serialization;

//http://answers.unity3d.com/questions/363477/c-how-to-setup-a-binary-serialization.html
//Required to guarantee a fixed serialization assembly name, which Unity likes to randomize on each compile
public sealed class VersionDeserializationBinder : SerializationBinder {
	public override Type BindToType(string assemblyName, string typeName) {
		if (!string.IsNullOrEmpty(assemblyName) && !string.IsNullOrEmpty(typeName)) {
			Type typeToDeserialize = null;
			assemblyName = Assembly.GetExecutingAssembly().FullName;
			typeToDeserialize = Type.GetType(string.Format("{0}, {1}", typeName, assemblyName));
			return typeToDeserialize;
		}
		return null;
	}
}
