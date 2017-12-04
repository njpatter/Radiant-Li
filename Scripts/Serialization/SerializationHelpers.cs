using UnityEngine;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;

/// <summary>
/// Helper functions to hide some of the setup to serialize objects.
/// </summary>
class SerializationHelpers {

	public static BinaryFormatter CreateBinaryFormatter() {
		BinaryFormatter bf = new BinaryFormatter();
		bf.Binder = new VersionDeserializationBinder();
		SurrogateSelector ss = new SurrogateSelector();
		StreamingContext context = new StreamingContext(StreamingContextStates.All);

		//if more surrogates are needed, add them here
		ss.AddSurrogate(typeof(Vector3), context, new Vector3SerializationSurrogate());

		bf.SurrogateSelector = ss;
		return bf;
	}
	
	public static void SerializeToBinaryFile(string filePath, object target) {
		Stream stream = new FileStream(filePath, FileMode.Create);
		SerializeToStream(stream, target);
		stream.Close();
	}
	
	public static void SerializeToStream(Stream stream, object target) {
		BinaryFormatter bf = CreateBinaryFormatter();
		bf.Serialize(stream, target);
	}
	
	public static object DeserializeFromBinaryFile(string filePath) {
		Stream stream = new FileStream(filePath, FileMode.Open);
		object ret = DeserializeFromStream(stream);
		stream.Close();
		return ret;
	}
	
	public static object DeserializeFromStream(Stream stream) {
		BinaryFormatter bf = CreateBinaryFormatter();
		return bf.Deserialize(stream);
	}
}
