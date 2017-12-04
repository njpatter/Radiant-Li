using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Callback for success or failure.
/// </summary>
public delegate void ResultCallback(string message);

/// <summary>
/// OAuth1 authentication & post.
/// </summary>
/// <description>
/// See <http://hueniverse.com/oauth/guide/authentication/> for testing.
/// </description>
public class OAuth1 : System.Object {
	/// <summary>
	/// Message sent when uploading data/etc. May be reused by
	/// external sites for progress updates.
	/// </summary>
	public const string kOnUpdateProgress = "OnUploadProgress";
	
	#region Access keys, formats, etc.
	/// <summary>
	/// OAuth public access key.
	/// </summary>
	const string kPublicTokenKeyFormat = "{0}PublicToken";
	
	/// <summary>
	/// OAuth secret access key.
	/// </summary>
	const string kSecretTokenKeyFormat = "{0}SecretToken";
	
	/// <summary>
	/// Prefix for public & private access tokens.
	/// </summary>
	string m_tokenIdentifier;
	
	/// <summary>
	/// Public consumer key; identifies an application.
	/// </summary>
	string m_publicConsumer;
	
	/// <summary>
	/// Private consumer key; identifies an application's signature.
	/// </summary>
	string m_secretConsumer;
	
	/// <summary>
	/// The final public access credential.
	/// </summary>
	string m_publicAccessToken;
	
	/// <summary>
	/// The final secret access credential.
	/// </summary>
	string m_secretAccessToken;
	
	/// <summary>
	/// Temporary public credential.
	/// </summary>
	string m_publicRequest;
	
	/// <summary>
	/// Temporary secret credential.
	/// </summary>
	string m_secretRequest;

	/// <summary>
	/// True if the user cancelled events.
	/// </summary>
	bool m_cancelling;
	#endregion
	
	/// <summary>
	/// Initializes a new instance of the <see cref="OAuth1"/> class.
	/// </summary>
	/// <param name='tokenIdentifier'>
	/// Identifier for player prefs.
	/// </param>
	/// <param name='consumerKey'>
	/// Consumer key from external site.
	/// </param>
	/// <param name='consumerSecret'>
	/// Consumer secret key from external site.
	/// </param>
	public OAuth1(string tokenIdentifier, string consumerKey, string consumerSecret) {
		m_publicConsumer  = consumerKey;
		m_secretConsumer  = consumerSecret;
		m_tokenIdentifier = tokenIdentifier;
		
		LoadCredentials(m_tokenIdentifier);
	}
	
	/// <summary>
	/// Returns true if we have access tokens.
	/// </summary>
	/// <returns>
	/// <c>true</c> if this instance is authorized; otherwise, <c>false</c>.
	/// </returns>
	public bool IsAuthorized() {
		return !string.IsNullOrEmpty(m_publicAccessToken) 
			&& !string.IsNullOrEmpty(m_secretAccessToken);	
	}
	
	/// <summary>
	/// Clears the temporary request credentials.
	/// </summary>
	public void ClearRequestCredentials() {
		m_publicRequest = m_secretRequest = "";	
	}
	
	/// <summary>
	/// Loads the final access credentials, if available.
	/// </summary>
	/// <param name='tokenIdentifier'>
	/// The external application identifier for player prefs.
	/// </param>
	void LoadCredentials(string tokenIdentifier) {
		string publicKey = string.Format(kPublicTokenKeyFormat, tokenIdentifier);
		string secretKey = string.Format(kSecretTokenKeyFormat, tokenIdentifier);
		
		m_publicAccessToken = PlayerPrefs.GetString(publicKey, "");
		m_secretAccessToken = PlayerPrefs.GetString(secretKey, "");
	}
	
	/// <summary>
	/// Saves the final access credentials.
	/// </summary>
	/// <param name='tokenIdentifier'>
	/// The external application identifier for player prefs.
	/// </param>
	void SaveCredentials(string tokenIdentifier) {
		string publicKey = string.Format(kPublicTokenKeyFormat, tokenIdentifier);
		string secretKey = string.Format(kSecretTokenKeyFormat, tokenIdentifier);
		
		PlayerPrefs.SetString(publicKey, m_publicAccessToken);
		PlayerPrefs.SetString(secretKey, m_secretAccessToken);
	}
	
	/// <summary>
	/// Requests authorization tokens, either temporary request or final access.
	/// </summary>
	/// <returns>
	/// The coroutine.
	/// </returns>
	/// <param name='fromUrl'>
	/// The authentication URL.
	/// </param>
	/// <param name='usingAuthorizationUrl'>
	/// The URL to display to the user after receiving a temporary 
	/// request token.
	/// </param>
	/// <param name='usingVerification'>
	/// The PIN/verification key from the external site's website.
	/// </param>
	/// <param name='OnSuccess'>
	/// Callback for successful authorization.
	/// </param>
	/// <param name='OnFailure'>
	/// Callback for failed authorization.
	/// </param>
	public IEnumerator RequestAuthorization(string fromUrl, string usingAuthorizationUrl,
		string usingVerification, ResultCallback OnSuccess, ResultCallback OnFailure) 
	{
		WWW request = WWWRequest(fromUrl, usingVerification);
		yield return request;
		
		if (!string.IsNullOrEmpty(request.error)) {
			Text.Error("Failed: {0}\nResponse: {1}", request.error, request.text);
			OnFailure(request.error);
			yield break;
		}
		
		string publicToken = System.Text.RegularExpressions.Regex.Match(request.text,
				@"oauth_token=([^&]+)").Groups[1].Value;
		string secretToken = System.Text.RegularExpressions.Regex.Match(request.text,
				@"oauth_token_secret=([^&]+)").Groups[1].Value;
		
		if (string.IsNullOrEmpty(publicToken) || string.IsNullOrEmpty(secretToken)) {
			Text.Error("Couldn't extract oauth token or token_secret.");
			OnFailure("Received incorrect headers. Try again later; if errors continue, Li may need to be updated.");
			yield break;
		}
		
		if (string.IsNullOrEmpty(usingVerification)) {
			// Success! Open up the authorization URL and ready the callback.
			m_publicRequest = publicToken;
			m_secretRequest = secretToken;
			Application.OpenURL(string.Format(usingAuthorizationUrl, m_publicRequest));
			OnSuccess("Request successful. Requesting authorization.");
		}
		else {
			m_publicAccessToken = publicToken;
			m_secretAccessToken = secretToken;
			SaveCredentials(m_tokenIdentifier);
			OnSuccess("Successfully authorized.");
		}
	}
	
	/// <summary>
	/// Posts data using OAuth authorization.
	/// </summary>
	/// <returns>
	/// The coroutine.
	/// </returns>
	/// <param name='toUrl'>
	/// The target posting URL.
	/// </param>
	/// <param name='withData'>
	/// The JSON-encoded data; sent as a POST body field.
	/// </param>
	/// <param name='OnFailure'>
	/// Callback for failed transmission.
	/// </param>
	/// <param name='OnSuccess'>
	/// Callback for successful transmission (but not
	/// necessarily correctly handled by the external
	/// site).
	/// </param>
	public IEnumerator PostData(string toUrl, Json withData,
		ResultCallback OnSuccess, ResultCallback OnFailure) 
	{
		Dictionary<string, string> parameters = new Dictionary<string, string>();
		AddDefaultOAuthParams(parameters, null);
		
		Dictionary<string, string> headers = new Dictionary<string, string>();
		headers["Authorization"] = GetFinalOAuthHeader("POST", toUrl, parameters);
		
		byte[] cookedData = System.Text.Encoding.UTF8.GetBytes(withData.ToString());
		WWW web = new WWW(toUrl, cookedData, headers);
		while (!Mathf.Approximately(web.uploadProgress, 1.0f)) {
			Dispatcher<float>.Broadcast(kOnUpdateProgress, web.uploadProgress);
			yield return null;
		}
		Dispatcher<float>.Broadcast(kOnUpdateProgress, 1.0f);
		m_cancelling = false;
		Dispatcher<string, string, PanelController.Handler, PanelController.Handler>.Broadcast(PanelController.kEventShowProgress,
				OAuth1.kOnUpdateProgress, 
		        "Downloading response {0:0%}...",
		        delegate() {},
				delegate() { m_cancelling = true; }
		);
		while (!m_cancelling && !Mathf.Approximately(web.progress, 1.0f)) {
			Dispatcher<float>.Broadcast(kOnUpdateProgress, web.progress);
			yield return null;
		}
		if (m_cancelling) {
			yield break;
		}

		Dispatcher<float>.Broadcast(kOnUpdateProgress, 1.0f);
		if (!string.IsNullOrEmpty(web.error)) OnFailure(web.error);
		else OnSuccess(web.text);
	}
	
	/// <summary>
	/// Returns the WWW connection.
	/// </summary>
	/// <returns>
	/// The WWW connection.
	/// </returns>
	/// <param name='fromUrl'>
	/// The target URL to POST to.
	/// </param>
	/// <param name='withVerification'>
	/// The pin/verification string; null/empty requests 
	/// temporary credentials.
	/// </param>
	WWW WWWRequest(string fromUrl, string withVerification) {
		// Prevent a Unity error from an empty body.
		byte[] empty = new byte[1];
		empty[0] = 0;
		
		Dictionary<string, string> parameters = new Dictionary<string, string>();
		AddDefaultOAuthParams(parameters, withVerification);
		
		Dictionary<string, string> headers = new Dictionary<string, string>();
		headers["Authorization"] = GetFinalOAuthHeader("POST", fromUrl, parameters);
		
		return new WWW(fromUrl, empty, headers);
	}
	
	/// <summary>
	/// Adds the default OAuth parameters.
	/// </summary>
	/// <param name='toParameters'>
	/// Header parameters we're filling.
	/// </param>
	/// <param name='usingVerification'>
	/// The PIN/verification string, if any.
	/// </param>
	void AddDefaultOAuthParams(Dictionary<string, string> toParameters, 
		string usingVerification)
	{
		toParameters["oauth_consumer_key"]     = m_publicConsumer;
		toParameters["oauth_nonce"]            = Random.Range(0, 999999999).ToString();
		toParameters["oauth_signature_method"] = "HMAC-SHA1";
		toParameters["oauth_timestamp"]        = UnixTimestamp();
		if (!string.IsNullOrEmpty(m_publicAccessToken)) {
			toParameters["oauth_token"] = m_publicAccessToken;
		}
		else if (string.IsNullOrEmpty(usingVerification)) {
			toParameters["oauth_token"] = "";
		}
		else {
			toParameters["oauth_token"]    = m_publicRequest;
			toParameters["oauth_verifier"] = usingVerification;
		}
		toParameters["oauth_version"] = "1.0";
	}
	
	/// <summary>
	/// Gets the final OAuth header string.
	/// </summary>
	/// <returns>
	/// The final OAuth header, ready for WWW.
	/// </returns>
	/// <param name='usingProtocol'>
	/// Should generally be "POST".
	/// </param>
	/// <param name='forUrl'>
	/// The target URL.
	/// </param>
	/// <param name='withParameters'>
	/// The HTTP headers to convert.
	/// </param>
	string GetFinalOAuthHeader(string usingProtocol, string forUrl, 
		Dictionary<string, string> withParameters)
	{
		System.Text.StringBuilder buffer = 
			new System.Text.StringBuilder("OAuth realm=\"" + forUrl + "\",");
		
		string signature = GenerateSignature(usingProtocol, forUrl, withParameters);
		foreach (KeyValuePair<string, string> aPair in withParameters) {
			// Note we're not URL encoding anything…because they should already
			// be encoded by this point.
			buffer.AppendFormat("{0}=\"{1}\",", aPair.Key, aPair.Value);
		}
		buffer.AppendFormat("oauth_signature=\"{0}\"", UrlEncode(signature));
		
		return buffer.ToString();
	}
	
	/// <summary>
	/// Generates the OAUTH1 signature.
	/// </summary>
	/// <returns>
	/// The signature string.
	/// </returns>
	/// <param name='usingProtocol'>
	/// Should be POST.
	/// </param>
	/// <param name='forUrl'>
	/// The target URL for the authentication.
	/// </param>
	/// <param name='withParameters'>
	/// The source HTTP headers, including oauth_* headers.
	/// </param>
	string GenerateSignature(string usingProtocol, string forUrl,
		Dictionary<string, string> withParameters) 
	{
		// Create the base string.
		System.Text.StringBuilder signatureBaseString = new System.Text.StringBuilder();
		signatureBaseString.AppendFormat(System.Globalization.CultureInfo.InvariantCulture,
			"{0}&{1}&", usingProtocol, UrlEncode(new System.Uri(forUrl).ToString()));
		foreach (KeyValuePair<string, string> aPair in withParameters) {
			signatureBaseString.AppendFormat(@"{0}%3D{1}%26", aPair.Key, UrlEncode(aPair.Value));
		}
		// Remove the trailing %26
		signatureBaseString.Remove(signatureBaseString.Length - 3, 3);
		
		// Create the hash key.
		string requestSecret = "";
		if (!string.IsNullOrEmpty(m_secretAccessToken)) requestSecret = m_secretAccessToken;
		else if (!string.IsNullOrEmpty(m_secretRequest)) requestSecret = m_secretRequest;
		
		string key = string.Format(System.Globalization.CultureInfo.InvariantCulture,
			@"{0}&{1}", m_secretConsumer, UrlEncode(requestSecret));
			
		System.Security.Cryptography.HMACSHA1 hash 
			= new System.Security.Cryptography.HMACSHA1(System.Text.Encoding.ASCII.GetBytes(key));
		byte[] signatureBytes = hash.ComputeHash(
			System.Text.Encoding.ASCII.GetBytes(signatureBaseString.ToString()));
		return System.Convert.ToBase64String(signatureBytes);
	}
	
	#region Utility methods
	/// <summary>
	/// Returns the current Unix timestamp string.
	/// </summary>
	/// <returns>
	/// The timestamp.
	/// </returns>
	static string UnixTimestamp() {
		return ((long)((System.DateTime.UtcNow 
			- new System.DateTime(1970, 1, 1, 0, 0, 0, 0)).TotalSeconds)).ToString();
	}
	
	/// <summary>
	/// Encodes the URL according to RFC 3986 §2.1.
	/// </summary>
	/// <description>
	/// See <https://dev.twitter.com/docs/auth/percent-encoding-parameters>
	/// for the general algorithm.
	/// </description>
	/// <returns>
	/// The encode.
	/// </returns>
	/// <param name='aValue'>
	/// A value.
	/// </param>
	public static string UrlEncode(string aValue) {
		if (string.IsNullOrEmpty(aValue)) return string.Empty;
		
		System.Text.StringBuilder buffer = new System.Text.StringBuilder();
		
		foreach (byte aByte in System.Text.Encoding.UTF8.GetBytes(aValue)) {
			int theValue = (int)aByte;
			if ((theValue >= 0x30 && theValue <= 0x39)
				|| (theValue >= 0x41 && theValue <= 0x5A)
				|| (theValue >= 0x61 && theValue <= 0x7A)
				|| theValue == 0x2D 
				|| theValue == 0x2E
				|| theValue == 0x5F
				|| theValue == 0x7E)
			{
				// Don't need to worry about it.
				buffer.Append((char)aByte);
			}
			else {
				buffer.AppendFormat("%{0:X2}", (int)aByte);	
			}
		}
		return buffer.ToString();
	}
	#endregion
}
