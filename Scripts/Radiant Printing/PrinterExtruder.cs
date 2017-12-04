using UnityEngine;
using System.Collections;

/// <summary>
/// An extruder.
/// </summary>
[System.Serializable]
public class PrinterExtruder : PrinterMotor {
	public const float kBaseLayerScale = 0.9f;
	public const float kDefaultLayerScale = 1.0f;
	public const float kFilamentDiameter = 1.75f;
	public const int kDefaultTickRate = 272; //192;//17;

	//const float kNozzleWidth = 0.440322f;
	public const float kStandardNozzleWidth = 0.35f;
	const float kDriveGearDiameterInMm = 10.345f;
	const float kOutlineVolumeMultiplier = 6f;
	const float kDriveGearCircumferenceInMm = Mathf.PI * kDriveGearDiameterInMm;
	const float kExtrusionRingStepMultiple = 9.4523f * 0.67f;

	const float kRateConstant = kExtrusionRingStepMultiple; // * kNozzleWidth / kStandardNozzleWidth;

	public override int maxAccelInStepsPerSec {
		get {
			return kMaxAccelInStepsPerSec_Extruders * (int)stepSize;
		}
	}
  	float m_rateConstant = kRateConstant;

	public static float layerScale = kDefaultLayerScale;

	public int ringNumber;
	public int firstLayerTemperature = 210;
	public int targetTemperatureC = 220;

	public int materialNumber = 1;

	public int relativeRing;
	public int stepOffset;
	public int extrusionRate;
	public int extrusionNumber;
	public StepSize standardExtrusionStepSize;

	public int pressureRequired = 0;
	public int stepsExtruded = 0;

	public bool isPrinting = false;

	/// <summary>
	/// Clones this instance.
	/// </summary>
	public override PrinterMotor Clone() {
		PrinterExtruder result = new PrinterExtruder();
		Populate(result);
		return result;
	}

	/// <summary>
	/// Clones this instance.
	/// </summary>
	public void Populate(PrinterExtruder result) {
		base.Populate(result);
		result.ringNumber = ringNumber;
		result.targetTemperatureC = targetTemperatureC;
		result.firstLayerTemperature = firstLayerTemperature;
		result.materialNumber = materialNumber;
	}

	public void SetRelativeLocation(int absoluteRing) {
		relativeRing = ringNumber + absoluteRing;
		//Text.Log(@"Extruder motor {0} relatively at {1}.",
		//	id, relativeRing);
	}

	/// <summary>
	/// Distances from plat center in mm; used by visualizer.
	/// </summary>
	/// <returns>
	/// The from plat center in mm.
	/// </returns>
	public float DistanceFromPlatCenterInMm(float relativePosition) {
		// Note that we can't use the relative ring because it
		// only gets updated for the hidden, working printer in
		// PrintController.

		return ringNumber * kStandardNozzleWidth + relativePosition;
	}

	/// <summary>
	/// Sets the default extrusion rate based on the relative ring number.
	/// </summary>
	/// <param name='ticksPerRotation'>
	/// The ticks (includes step-rate) per platform rotation.
	/// </param>
	/// <param name='numberOfArcs'>
	/// Number of arcs.
	/// </param>
	public int SetExtrusionRate(float ticksPerRotation, int numberOfArcs) {
		float numExtrusions = Mathf.Max(layerScale * Mathf.Abs(relativeRing) * m_rateConstant, 1f);
		extrusionRate = Mathf.RoundToInt(ticksPerRotation / numExtrusions);

		return extrusionRate;
	}

  /// <summary>
  /// Scale the amount extruded.
  /// </summary>
  public void ScaleRateConstant(float byAmount) {
		m_rateConstant = kRateConstant * byAmount;
	}

	public int StepsForMm(float mm) {
		float rotationsRequired = Mathf.Abs(mm) / kDriveGearCircumferenceInMm;
		return Mathf.RoundToInt(rotationsRequired * (float)stepsPerRotation);
	}

	public float MmPerStep() {
		return (kDriveGearCircumferenceInMm / (float)stepsPerRotation);
	}

	public float volumePerStep {
		get {
			//Debug.Log(this.MmPerStep() * Mathf.PI * (kFilamentDiameter * kFilamentDiameter / 4) / kOutlineVolumeMultiplier);
			return this.MmPerStep() * Mathf.PI * (kFilamentDiameter * kFilamentDiameter / 4) / kOutlineVolumeMultiplier;
		}
	}

	public float DistancePrintedPerStep(float layerThickness, float trackWidth) {
		return volumePerStep / (layerThickness * trackWidth);
	}
}
