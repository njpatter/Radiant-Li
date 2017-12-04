using UnityEngine;
using System;
using System.Runtime.InteropServices;
using System.Text;

public class FileDialogs {
	
	const int kMaxPath = 260;
	
#if UNITY_STANDALONE_WIN || UNITY_METRO
	[DllImport("kernel32.dll")]
	public static extern IntPtr LoadLibrary(string dllToLoad);

	[DllImport("kernel32.dll")]
	public static extern IntPtr GetProcAddress(IntPtr hModule, string procedureName);	
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate bool OpenFileDialogDelegate(StringBuilder fileName, int maxLen, string filter);
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate bool SaveFileDialogDelegate(StringBuilder fileName, int maxLen, string ext);

	private static OpenFileDialogDelegate OpenFileDialog;
	private static SaveFileDialogDelegate SaveFileDialog;
#else
	class OSX_PreLion {
		[DllImport("FileDialogsPreLion")]
	    public static extern bool SaveFileDialog(StringBuilder fileName, int maxLen, string extension);
		
		[DllImport("FileDialogsPreLion")]
	    public static extern bool OpenFileDialog(StringBuilder fileName, int maxLen, string filter);
	}
	
	class OSX_PostLion {
		[DllImport("FileDialogsPostLion")]
	    public static extern bool SaveFileDialog(StringBuilder fileName, int maxLen, string extension);
		
		[DllImport("FileDialogsPostLion")]
	    public static extern bool OpenFileDialog(StringBuilder fileName, int maxLen, string filter);
	}
	
	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate bool OpenFileDialogDelegate(StringBuilder fileName, int maxLen, string filter);

	[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
	private delegate bool SaveFileDialogDelegate(StringBuilder fileName, int maxLen, string ext);

	private static OpenFileDialogDelegate OpenFileDialog;
	private static SaveFileDialogDelegate SaveFileDialog;

#endif
	
	static bool inited = false;
	
	static void Init() {
		if (!inited) {
#if UNITY_STANDALONE_WIN || UNITY_METRO
			string path = UnityEngine.Application.dataPath + "/Plugins/";
#if UNITY_EDITOR
			path += "x86/";
#endif
			if (LoadLibrary(path + "msvcr100.dll") == IntPtr.Zero)
				Text.Error("failed to load msvcr100");
			IntPtr lib = LoadLibrary(path + "FileDialogs.dll");
			if (lib == IntPtr.Zero) {
				Text.Error("Failed to load FileDialogs");
				return;
			}
			
			IntPtr proc = GetProcAddress(lib, "OpenFileDialog");
			OpenFileDialog = (OpenFileDialogDelegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(OpenFileDialogDelegate));
			
			proc = GetProcAddress(lib, "SaveFileDialog");
			SaveFileDialog = (SaveFileDialogDelegate)Marshal.GetDelegateForFunctionPointer(proc, typeof(SaveFileDialogDelegate));
#elif UNITY_STANDALONE_OSX
			OsVersionChecker checker = new OsVersionChecker();
			if (checker.currentVersion == OsVersion.SnowLeopard) {
				OpenFileDialog = OSX_PreLion.OpenFileDialog;
				SaveFileDialog = OSX_PreLion.SaveFileDialog;
			}
			else {
				OpenFileDialog = OSX_PostLion.OpenFileDialog;
				SaveFileDialog = OSX_PostLion.SaveFileDialog;
				
			}
			
#endif
			inited = true;
		}
	}

	public static bool ShowOpenFileDialog(out string fileName, string[] extensions, string[] extensionNames) {
		if (extensions.Length != extensionNames.Length)
			throw new System.Exception("ShowOpenFileDialog: extensions and extensionNames need to be the same length.");
		
		Init();
		
		fileName = "";
		string filter = "";
		
#if UNITY_STANDALONE_WIN || UNITY_METRO
		for (int i = 0; i < extensions.Length; ++i) {
			filter += extensionNames[i] + "\0";
			filter += "*." + extensions[i] + "\0";
		}
		
		filter += "\0";
#else
		filter = String.Join(":", extensions);
#endif
		
		StringBuilder sb = new StringBuilder(kMaxPath);
		bool ret = OpenFileDialog(sb, kMaxPath, filter);
		
		if (ret)
			fileName = sb.ToString();
		
		return ret;
	}
	
	public static bool ShowSaveFileDialog(out string fileName, string extension) {
		Init();

		fileName = "";
		StringBuilder sb = new StringBuilder(kMaxPath);
		
		bool ret = SaveFileDialog(sb, kMaxPath, extension);
		
		if (ret)
			fileName = sb.ToString();
		
		return ret;
	}
}
