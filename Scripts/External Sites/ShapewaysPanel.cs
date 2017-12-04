using UnityEngine;
using System.Collections;

/// <summary>
/// Controls the Shapeways UI.
/// </summary>
public class ShapewaysPanel : MonoBehaviour {
	/// <summary>
	/// Event to open Shapeways panel.
	/// </summary>
	public const string kOpenShapeways = "Shapeways";
	
	/// <summary>
	/// The model title.
	/// </summary>
	public UILabel title;
	
	/// <summary>
	/// The model's tags.
	/// </summary>
	public UILabel tags;
	
	/// <summary>
	/// The rights box.
	/// </summary>
	public UIToggle rightsBox;
	
	/// <summary>
	/// The terms of service acceptance box.
	/// </summary>
	public UIToggle termsBox;
	
	/// <summary>
	/// Indicates whether or not the model should be publicly.
	/// viewable.
	/// </summary>
	public UIToggle publicBox;
	
	/// <summary>
	/// Indicates whether or not the model should be downloadable.
	/// </summary>
	public UIToggle downloadBox;
	
	/// <summary>
	/// Attempts to upload to Shapeways.
	/// </summary>
	public GameObject okButton;
	
	/// <summary>
	/// Closes the display.
	/// </summary>
	public GameObject cancelButton;
	
	/// <summary>
	/// The shapeways source.
	/// </summary>
	public Shapeways shapeways;
	
	#region Initialization & cleanup
	/// <summary>
	/// Starts listening for menu options.
	/// </summary>
	public void Awake() {
		Dispatcher.AddListener(kOpenShapeways, OnDisplay);
		gameObject.SetActive(false);
	}
	
	/// <summary>
	/// Stops listening.
	/// </summary>
	void OnDestroy() {
		Dispatcher.RemoveListener(kOpenShapeways, OnDisplay);
	}
	#endregion
	
	#region Activation handling
	/// <summary>
	/// Displays the panel.
	/// </summary>
	void OnDisplay() {
		gameObject.SetActive(true);	
	}
	
	/// <summary>
	/// Clears information and starts listening for input.
	/// </summary>
	void OnEnable() {
		// NOTE: In the future, we want to keep these values 
		// instead of resetting them.
		title.text = "";
		
		tags.text = "Radiant Li,";
		rightsBox.value = false;
		termsBox.value = false;
		publicBox.value = false;
		downloadBox.value = false;
		
		UIEventListener.Get(okButton).onClick += OnOkClicked;
		UIEventListener.Get(cancelButton).onClick += OnCancelClicked;
	}
	
	/// <summary>
	/// Stops listening for input.
	/// </summary>
	void OnDisable() {
		UIEventListener.Get(okButton).onClick     -= OnOkClicked;
		UIEventListener.Get(cancelButton).onClick -= OnCancelClicked;
	}
	#endregion
	
	#region User response
	/// <summary>
	/// Verifies IP Rights & terms of service before starting 
	/// the upload process.
	/// </summary>
	/// <param name='source'>
	/// Ignored.
	/// </param>
	void OnOkClicked(GameObject source) {
		Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Select);
		
		if (!rightsBox.value) {
			Dispatcher<string, PanelController.Handler>.Broadcast(PanelController.kEventPrompt, 
				"You may only upload models if you have rights to do so.",
				OnDisplay);
		}
		else if (!termsBox.value) {
			Dispatcher<string, PanelController.Handler>.Broadcast(PanelController.kEventPrompt,
				"You must accept Shapeway's terms before uploading models.",
				OnDisplay);
		}
		else {
			shapeways.StartCoroutine(shapeways.UploadModel(title.text, 
				tags.text, publicBox.value, downloadBox.value));
		}
		gameObject.SetActive(false);
	}
	
	/// <summary>
	/// Closes the panel, relaunching the menu.
	/// </summary>
	/// <param name='source'>
	/// Source.
	/// </param>
	void OnCancelClicked(GameObject source) {
		Dispatcher<Sfx>.Broadcast(AudioManager.kPlaySfx, Sfx.Select);
		gameObject.SetActive(false);
		Dispatcher<Panel>.Broadcast(PanelController.kEventOpenPanel, Panel.Menu);
	}
	#endregion
}
