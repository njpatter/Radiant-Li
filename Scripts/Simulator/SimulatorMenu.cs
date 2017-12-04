using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// The printing menu.
/// </summary> 
public class SimulatorMenu : MonoBehaviour {
	#if !UNITY_IOS && !UNITY_ANDROID
	public PrinterController pc;
	Printer m_workingPrinter;
	
	const float kButtonWidth  = 128.0f;
	const float kButtonHeight = 24.0f;
	const float kMargin       = 12.0f;

	delegate void StateDelegate();
	StateDelegate HandleClosing;
	StateDelegate HandleMenu;
	
	public SerialController testSerial;
	
	/// <summary>
	/// Caches components and starts listening.
	/// </summary>
	void Awake() {
		HandleMenu = NormalMenu;
	}
	
	void Start() {
		m_workingPrinter = pc.originalPrinter.Clone();
		pc.InitializeMotors();
	}
	
	void OnDestroy() {
		if (HandleMenu == PrintingMenu) {
			Dispatcher<float>.RemoveListener(PrinterController.kOnPrintingProgress, OnPrintingProgress);
		}
	}
	
	/// <summary>
	/// Do nothing.
	/// </summary>
	void Nop() { }
	
	/// <summary>
	/// Displays our menu.
	/// </summary>
	void OnGUI() { HandleMenu(); }
	/*
	string m_verificationCode = "";
	void AuthorizationMenu() {
		float yInitial = (Screen.height - (kButtonHeight + kMargin) * 6) / 2.0f;
		Rect buttonRect = new Rect((Screen.width - kButtonWidth) / 4.0f, 
			yInitial, kButtonWidth, kButtonHeight);
		GUI.Label(buttonRect, @"Enter verification code: ");
		buttonRect.x += kButtonWidth;
		m_verificationCode = GUI.TextField(buttonRect, m_verificationCode);
		buttonRect.y += kButtonHeight;
		if (GUI.Button(buttonRect, "OK")) {
			// Try to authorize here
			StartCoroutine(OAuth1.GetAccessToken(ExternalSite.ShapeWays, m_verificationCode, AccessResponse));
			HandleMenu = NormalMenu;
		}
	}
	*/
	
	void PrintingMenu() {

	}
	
	void NormalMenu() {
		float yInitial = (Screen.height - (kButtonHeight + kMargin) * 6) / 2.0f;
		Rect buttonRect = new Rect((Screen.width - kButtonWidth) / 4.0f, 
			yInitial, kButtonWidth, kButtonHeight);
		
		// Left column ////////////////////////////////////////////////////////
		if (GUI.Button(buttonRect, m_workingPrinter.isAwake ? "Sleep" : "Wake")) {
			if (!m_workingPrinter.isAwake) {
				pc.WakePrinter(m_workingPrinter);
			}
			else {
				pc.SleepPrinter(m_workingPrinter);
			}
		}

		buttonRect.y += kButtonHeight + kMargin;
		if (GUI.Button(buttonRect, "Print Test")) {
			// Print
			pc.SchedulePrint(VoxelBlob.NewTestDisc());
			HandleMenu = PrintingMenu;
			Dispatcher<float>.AddListener(PrinterController.kOnPrintingProgress, OnPrintingProgress);
		}
		
		buttonRect.y += kButtonHeight + kMargin;
		if (GUI.Button(buttonRect, @"Print Test Scene")) {
			string filePath;
			string[] extensions = new string[] { "radiant" };
			string[] extensionNames = new string[] { "Radiant scene" };
			
			if (FileDialogs.ShowOpenFileDialog(out filePath, extensions, extensionNames)) {
				System.IO.FileStream stream = new System.IO.FileStream(filePath, System.IO.FileMode.Open);
				VoxelBlob blob = VoxelBlob.NewFromFile(stream, false);
				pc.SchedulePrint(blob);
				HandleMenu = PrintingMenu;
				Dispatcher<float>.AddListener(PrinterController.kOnPrintingProgress, OnPrintingProgress);
			}
			else Text.Log(@"Aborted opening.");
		}
	
		/*
		buttonRect.y += kButtonHeight + kMargin;
		if (GUI.Button(buttonRect, "Request Token Test")) {
			StartCoroutine(OAuth1.GetRequestToken(ExternalSite.ShapeWays, RequestResponse));
			HandleMenu = AuthorizationMenu;
		}
		*/
		
		/*
		GUI.enabled = !m_isSaved;
		buttonRect.y += kButtonHeight + kMargin;
		if (GUI.Button(buttonRect, "Save")) {
			string filePath = NativePanels.SaveFileDialog("model.radiant", "radiant");
			SaveBlob(filePath);
			Text.Log("Saved " + filePath);
			m_isSaved = true;
			Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Select);
		}
		
		GUI.enabled = true;
		buttonRect.y += kButtonHeight + kMargin;
		if (GUI.Button(buttonRect, "Load")) {
			//PlayerPrefs.SetString(VoxelBlob.kSerializedKey, serializedBlob);
			string filePath = NativePanels.OpenFileDialog(new string[]{"radiant"});
			LoadBlob(filePath);
			m_isSaved = true;
			Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Select);
		}
		*/
		
		// Right column ///////////////////////////////////////////////////////
		buttonRect.x = (Screen.width - kButtonWidth) * 0.75f;
		buttonRect.y = yInitial;
		buttonRect.height = kButtonHeight;
		buttonRect.width = kButtonWidth / 3.0f;
		buttonRect.x += buttonRect.width + kMargin;
		if (GUI.Button(buttonRect, "^")) {
			pc.BeginMotorChanges(m_workingPrinter);
			pc.MoveVerticallyBy(-10.0f, Change.Execute);
			pc.EndMotorChanges();
		}
		
		buttonRect.y += kButtonHeight + kMargin;
		buttonRect.x = buttonRect.x - buttonRect.width - kMargin;
		if (GUI.Button(buttonRect, "<")) {
			pc.BeginMotorChanges(m_workingPrinter);
			pc.MoveHorizontallyBy(10.0f, Change.Execute);
			pc.EndMotorChanges();
		}
		
		buttonRect.x += buttonRect.width + kMargin;
		if (GUI.Button(buttonRect, "v")) {
			pc.BeginMotorChanges(m_workingPrinter);
			pc.MoveVerticallyBy(10.0f, Change.Execute);
			pc.EndMotorChanges();
		}
		
		buttonRect.x += buttonRect.width + kMargin;
		if (GUI.Button(buttonRect, ">")) {
			pc.BeginMotorChanges(m_workingPrinter);
			pc.MoveHorizontallyBy(-10.0f, Change.Execute);
			pc.EndMotorChanges();
		}
	}
	
	void OnPrintingProgress(float amount) {
		if (Mathf.Approximately(amount, 1.0f)) {
			HandleMenu = NormalMenu;
			Dispatcher<float>.RemoveListener(PrinterController.kOnPrintingProgress, OnPrintingProgress);
		}
	}	
	
	/// <summary>
	/// Displays a slider with a formatted label; updates prefs if needed.
	/// </summary>
	/// <param name='bounds'>
	/// Bounds.
	/// </param>
	/// <param name='format'>
	/// Format.
	/// </param>
	/// <param name='initialValue'>
	/// Initial value.
	/// </param>
	/// <param name='minValue'>
	/// Minimum value.
	/// </param>
	/// <param name='maxValue'>
	/// Max value.
	/// </param>
	float Slider(Rect bounds, string format, float initialValue, float minValue, float maxValue, string key) {
		// +---------------------------------+
		// | LabelFormat                     |
		// |  -------O---------------------  |
		// +---------------------------------+
		GUI.BeginGroup(bounds);
		bounds.x = bounds.y = 0;
		GUI.Box(bounds, "");
		
		bounds.x = kMargin;
		bounds.y = kMargin / 2;
		bounds.width -= kMargin * 2;
		bounds.height -= kMargin;
		GUI.Label(bounds, string.Format(format, initialValue));
		bounds.y += kButtonHeight;
		bounds.height -= kButtonHeight;
		float result = GUI.HorizontalSlider(bounds, initialValue, minValue, maxValue);
		GUI.EndGroup();
		
		if (result != initialValue) PlayerPrefs.SetFloat(key, result);
		return result;
	}

	void SaveBlob(string path) {
		Text.Warning("Not implemented.");
		/*
		byte[] packedBlob = manager.m_blob.ToBytes();
		
		using (System.IO.BinaryWriter fout = new System.IO.BinaryWriter(System.IO.File.Open(path, System.IO.FileMode.Create))) {
			fout.Write(packedBlob);
			fout.Close();
		}
		*/
	}
	
	void LoadBlob(string path) {
		Text.Warning("Not implemented.");
		/*
		StartCoroutine(VoxelBlob.NewFromFile(System.IO.File.OpenRead(path), LoadComplete));
		Text.Log("Loading blob at " + path);
		*/
	}
	
	void LoadComplete(VoxelBlob loadedBlob) {
	}
#endif
}
