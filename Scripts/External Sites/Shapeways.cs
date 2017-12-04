using UnityEngine;
using System.Collections;

public class Shapeways : MonoBehaviour {
	#region OAuth configuration
	const string kConsumerKey    = "7a3a7af890731cf8d07463dd384645863519983e";
	const string kConsumerSecret = "8ede871c3ddccabea8d6f32e3e9e93ec0fe7b83f";
	const string kPrefix         = "Shapeways";
	#endregion
	
	#region OAuth URLs
	const string kRequestUrl       = "http://api.shapeways.com/oauth1/request_token/v1";
	const string kAuthorizationUrl = "http://api.shapeways.com/login?oauth_token={0}";
	const string kAccessUrl        = "http://api.shapeways.com/oauth1/access_token/v1";
	#endregion
	
	public string label {
		get { return "Shapeways"; }	
	}
	
	public MeshManager manager;
	
	OAuth1 m_authority;
	MeshPattern m_pattern;
	System.IO.MemoryStream m_stlStream;
	bool m_stlReady;
	bool m_cancelling;
	
	void Awake() {
		m_authority = new OAuth1(kPrefix, kConsumerKey, kConsumerSecret);
		Dispatcher<float>.AddListener(MeshPattern.kOnExportUpdate, StlExportUpdate);
	}
	
	void OnDestroy() {
		Dispatcher<float>.RemoveListener(MeshPattern.kOnExportUpdate, StlExportUpdate);
	}

	#region Interaction with the menu
	public void CheckAuthorization() {
		// User just clicked the "Upload to Shapeways" button.
		// If they're authorized, open up the upload panel.
		// If they're not, try to authorize them.
		
		// Start converting to STL right away.
		m_pattern = new MeshPattern();
		m_stlStream = new System.IO.MemoryStream();
		m_stlReady = false;
		m_pattern.SaveStlFromBlob(manager.m_blob, m_stlStream, false);
		
		if (m_authority.IsAuthorized()) {
			Dispatcher.Broadcast(ShapewaysPanel.kOpenShapeways);
		}
		else {
			StartCoroutine(m_authority.RequestAuthorization(kRequestUrl, kAuthorizationUrl,
				null, OpenPinMenu, DisplayError));
		}
	}
	
	void OpenPinMenu(string message) {
		// Prompt the user to enter their verification code.
		Dispatcher<string, PanelController.StringHandler,PanelController.StringHandler>.Broadcast(
			PanelController.kEventRequest, 
			"Enter your verification number below:", OnVerificationEntered, 
			OnVerificationCanceled);
	}
	
	void DisplayError(string reason) {
		// Make the menu visible again.	
		Dispatcher<string, PanelController.Handler>.Broadcast(PanelController.kEventPrompt,
			reason, OnErrorAcknowledged);
		
		Text.Error(reason);
	}
	
	void OnVerificationEntered(string verification) {
		Text.Log(@"Using verification number: {0}", verification);
		StartCoroutine(m_authority.RequestAuthorization(kAccessUrl, null,
				verification, OnCredentialsOk, DisplayError));
	}
	
	void OnVerificationCanceled(string verification) {
		Dispatcher<Panel>.Broadcast(PanelController.kEventOpenPanel, Panel.Menu);
		m_authority.ClearRequestCredentials();
	}
		
	void OnErrorAcknowledged() { 
		Dispatcher<Panel>.Broadcast(PanelController.kEventOpenPanel, Panel.Menu);
		m_authority.ClearRequestCredentials();
	}
	
	void OnCredentialsOk(string message) {
		Text.Log(message);
		Dispatcher.Broadcast(ShapewaysPanel.kOpenShapeways);
	}
	#endregion
	
	#region Shapeways panel functions
	public IEnumerator UploadModel(string title, string tags, bool isPublic, bool canDownload) {
		if (!m_stlReady) {
			m_cancelling = false;
			Dispatcher<string, string, PanelController.Handler, PanelController.Handler>.Broadcast(
				PanelController.kEventShowProgress,
				OAuth1.kOnUpdateProgress, "Processing ({0:0%} completed)...", 
				delegate() {},
				delegate() { m_cancelling = true; Scheduler.StopCoroutines(m_pattern); }
			);
			while (!m_cancelling && !m_stlReady) {
				Dispatcher<float>.Broadcast(OAuth1.kOnUpdateProgress, m_pattern.loadProgress);
				yield return null;
			}
			if (m_cancelling) {
				yield break;
			}
			Dispatcher<float>.Broadcast(OAuth1.kOnUpdateProgress, 1.0f);
		}
		
		Dispatcher<string, string, PanelController.Handler, PanelController.Handler>.Broadcast(
			PanelController.kEventShowProgress,
			OAuth1.kOnUpdateProgress, "Uploading ({0:0%} completed)...", 
			delegate() {}, 
			delegate() { m_cancelling = true; Scheduler.StopCoroutines(this); }
		);

		byte[] fileData = m_stlStream.ToArray();
		string fileBase64Data = System.Convert.ToBase64String(fileData);
		
		title = title.Replace(" ", "").Trim();
		string fileName = title;
		if (string.IsNullOrEmpty(fileName)) {
			fileName = "Untitled.stl";
			title = "Untitled";
		}
		else if (!fileName.Contains(".")) fileName = fileName + ".stl";
		
		Json payload = new Json();
		payload.Add("file", OAuth1.UrlEncode(fileBase64Data));
		payload.Add("fileName", fileName);
		payload.Add("uploadScale", VoxelBlob.kVoxelSizeInMm * 0.001f);
		payload.Add("hasRightsToModel", 1);
		payload.Add("acceptTermsAndConditions", 1);
		payload.Add("title", title);
		payload.Add("isPublic", isPublic ? 1 : 0);
		payload.Add("isDownloadable", canDownload ? 1 : 0);
		payload.Add("tags", tags.Split(',', ';'));
		
		yield return Scheduler.StartCoroutine(m_authority.PostData("http://api.shapeways.com/models/v1", 
			payload, OnSuccess, OnFailure), this);
		m_stlStream.Close();
	}
	
	void OnSuccess(string results) {
		bool wasSuccessful = results.IndexOf("success") >= 0;
		if (wasSuccessful) {
			Dispatcher<string, PanelController.Handler>.Broadcast(PanelController.kEventPrompt,
				"Upload successful.", delegate () {  
					Dispatcher<Panel>.Broadcast(PanelController.kEventOpenPanel, Panel.Menu);
			});
		}
		else OnFailure(string.Format("Unexpected result: {0}", results));
	}
	
	void OnFailure(string error) {
		Dispatcher<string, PanelController.Handler>.Broadcast(PanelController.kEventPrompt,
				"Received an invalid response. Try again later.",
				OnResultAcknowledged);
		Text.Error("{0}", error);
	}
	
	void OnResultAcknowledged() {
		Dispatcher<Panel>.Broadcast(PanelController.kEventOpenPanel, Panel.Menu);
	}
	#endregion

	void StlExportUpdate(float amount) {
		if (Mathf.Approximately(amount, 1.0f)) {
			m_stlReady = true;
		}
	}
}
