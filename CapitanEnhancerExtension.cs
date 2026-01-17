using System.IO;
using Newtonsoft.Json.Linq;
using SwarmUI.Builtin_ComfyUIBackend;
using SwarmUI.Core;
using SwarmUI.Text2Image;
using SwarmUI.Utils;
using System.Collections.Generic;

// namespace NMTYP.Extensions.CapitanEnhancer;
namespace SwarmUI.Builtin_CapitanEnhancer;

public class CapitanEnhancerExtension : Extension
{
    public static T2IParamGroup CapitanGroup, CapitanPass2Group;

    public static T2IRegisteredParam<bool> EnablePass2, P1HighPass, P1Normalize, P1SelfAttn, P1LowVram, P2HighPass, P2Normalize, P2SelfAttn, P2LowVram;
    public static T2IRegisteredParam<double> P1Strength, P1Detail, P1Preserve, P1AttnStrength, P2Strength, P2Detail, P2Preserve, P2AttnStrength;
    public static T2IRegisteredParam<int> P1MLPMult, P1Seed, P2MLPMult, P2Seed;
    public static T2IRegisteredParam<string> P1Device, P2Device;

    public override void OnInit()
    {
		// Add the installable info for the comfy node backend ✅
		InstallableFeatures.RegisterInstallableFeature(new("Capitan Conditioning Enhancer", "capitan_enhancer", "https://github.com/capitan01R/Capitan-ConditioningEnhancer", "capitan01r", "This will install the Capitan Conditioning Enhancer ComfyUI node developed by Capitan01R.\nDo you wish to install?")); // https://github.com/mcmonkeyprojects/SwarmUI/blob/b6c6b7377b25fd820516648215b724a8ca63037f/src/Core/InstallableFeatures.cs
		
		ComfyUISelfStartBackend.ComfyNodeGitPins["Capitan-ConditioningEnhancer"] = "10f2ef29cd3ed47de84f45deaf27cd68cc251395"; // "Mapping of node folder names to exact git commits to maintain."; Example: ["ComfyUI-TeaCache"] = "b3429ef3dea426d2f167e348b44cd2f5a3674e7d"
		
		// Add the JS file, which manages the install button for the comfy node ✅
        ScriptFiles.Add("assets/capitanenhancer.js");		
		
		// Register the feature for the ComfyUI backend ✅
        ComfyUIBackendExtension.NodeToFeatureMap["CapitanAdvancedEnhancer"] = "capitan_enhancer"; // ComfyUI Node ID to internal feature ID. In NODE_CLASS_MAPPINGS of __init__.py
		
        CapitanGroup = new("Conditioning Enhancer", Toggles: true, Open: false, IsAdvanced: false);
        CapitanPass2Group = new("Conditioning Enhancer Second Pass", Toggles: false, Open: false, IsAdvanced: false, Parent: CapitanGroup);

        // --- PASS 1 PARAMETERS ---
        P1Strength = RegisterFloat("Strength", "Enhance strength (negative values reduce influence).", "0.05", -3.0, 2.0, 0.01, 1);
        P1Detail = RegisterFloat("Detail Boost", "Boosts fine details in the conditioning.", "1.0", 0.0, 3.0, 0.1, 2);
        P1Preserve = RegisterFloat("Preserve Original", "Blends back the original conditioning (1.0 = no change).", "0.0", 0.0, 1.0, 0.05, 3);
        P1AttnStrength = RegisterFloat("Attention Strength", "Strength of the self-attention layer.", "0.3", 0.0, 1.0, 0.05, 4);
        P1HighPass = RegisterBool("High Pass Filter", "Applies a high-pass filter to the refinement.", false, 5);
        P1Normalize = RegisterBool("Normalize", "Normalizes the embedding before processing.", true, 6);
        P1SelfAttn = RegisterBool("Self Attention", "Adds a self-attention pass.", false, 7);
        P1MLPMult = RegisterInt("MLP Multiplier", "Hidden layer size multiplier for the MLP.", 8, 1, 100, 8);
        P1Seed = RegisterInt("Seed", "Random seed for the MLP initialization.", 42, 0, int.MaxValue, 9);
        P1LowVram = RegisterBool("Low VRAM", "Uses FP16 for computation.", false, 10);
        P1Device = RegisterDevice("Device", "Target compute device.", 11);

        // --- TOGGLE FOR SECOND PASS ---
        EnablePass2 = T2IParamTypes.Register<bool>(new("[Capitan] Enable Second Pass", "Whether to chain a second enhancement pass with different settings.",
            "false", Group: CapitanGroup, FeatureFlag: "capitan_enhancer", OrderPriority: 20));

        // --- PASS 2 PARAMETERS ---
        P2Strength = RegisterFloat("Pass 2: Strength", "Pass 2: Enhance strength.", "0.05", -3.0, 2.0, 0.01, 21, CapitanPass2Group);
        P2Detail = RegisterFloat("Pass 2: Detail Boost", "Pass 2: Detail boost.", "1.0", 0.0, 3.0, 0.1, 22, CapitanPass2Group);
        P2Preserve = RegisterFloat("Pass 2: Preserve Original", "Pass 2: Blends back previous pass.", "0.0", 0.0, 1.0, 0.05, 23, CapitanPass2Group);
        P2AttnStrength = RegisterFloat("Pass 2: Attention Strength", "Pass 2: Attention strength.", "0.3", 0.0, 1.0, 0.05, 24, CapitanPass2Group);
        P2HighPass = RegisterBool("Pass 2: High Pass Filter", "Pass 2: High-pass toggle.", false, 25, CapitanPass2Group);
        P2Normalize = RegisterBool("Pass 2: Normalize", "Pass 2: Normalize toggle.", true, 26, CapitanPass2Group);
        P2SelfAttn = RegisterBool("Pass 2: Self Attention", "Pass 2: Self-attention toggle.", false, 27, CapitanPass2Group);
        P2MLPMult = RegisterInt("Pass 2: MLP Multiplier", "Pass 2: MLP multiplier.", 8, 1, 100, 28, CapitanPass2Group);
        P2Seed = RegisterInt("Pass 2: Seed", "Pass 2: Seed.", 43, 0, int.MaxValue, 29, CapitanPass2Group);
        P2LowVram = RegisterBool("Pass 2: Low VRAM", "Pass 2: Low VRAM toggle.", false, 30, CapitanPass2Group);
        P2Device = RegisterDevice("Pass 2: Device", "Pass 2: Device.", 31, CapitanPass2Group);

        WorkflowGenerator.AddStep(g =>
        {
            if (g.UserInput.TryGet(P1Strength, out _))
            {
                if (!g.Features.Contains("capitan_enhancer"))
                {
                    throw new SwarmUserErrorException("Capitan Enhancer parameters specified, but feature isn't installed");
                }

                // --- First Pass ---
                string node1 = g.CreateNode("CapitanAdvancedEnhancer", new JObject()
                {
                    ["conditioning"] = g.FinalPrompt,
                    ["enhance_strength"] = g.UserInput.Get(P1Strength),
                    ["detail_boost"] = g.UserInput.Get(P1Detail),
                    ["preserve_original"] = g.UserInput.Get(P1Preserve),
                    ["attention_strength"] = g.UserInput.Get(P1AttnStrength),
                    ["high_pass_filter"] = g.UserInput.Get(P1HighPass),
                    ["normalize"] = g.UserInput.Get(P1Normalize),
                    ["add_self_attention"] = g.UserInput.Get(P1SelfAttn),
                    ["mlp_hidden_mult"] = g.UserInput.Get(P1MLPMult),
                    ["seed"] = g.UserInput.Get(P1Seed),
                    ["low_vram"] = g.UserInput.Get(P1LowVram),
                    ["device"] = g.UserInput.Get(P1Device)
                });
                g.FinalPrompt = [node1, 0];

                // --- Second Pass (Conditional) ---
                if (g.UserInput.Get(EnablePass2))
                {
                    string node2 = g.CreateNode("CapitanAdvancedEnhancer", new JObject()
                    {
                        ["conditioning"] = g.FinalPrompt,
                        ["enhance_strength"] = g.UserInput.Get(P2Strength),
                        ["detail_boost"] = g.UserInput.Get(P2Detail),
                        ["preserve_original"] = g.UserInput.Get(P2Preserve),
                        ["attention_strength"] = g.UserInput.Get(P2AttnStrength),
                        ["high_pass_filter"] = g.UserInput.Get(P2HighPass),
                        ["normalize"] = g.UserInput.Get(P2Normalize),
                        ["add_self_attention"] = g.UserInput.Get(P2SelfAttn),
                        ["mlp_hidden_mult"] = g.UserInput.Get(P2MLPMult),
                        ["seed"] = g.UserInput.Get(P2Seed),
                        ["low_vram"] = g.UserInput.Get(P2LowVram),
                        ["device"] = g.UserInput.Get(P2Device)
                    });
                    g.FinalPrompt = [node2, 0];
                }
				
            }
        }, -7.5);
    }

    // Helper registration methods to reduce boilerplate
    private static T2IRegisteredParam<double> RegisterFloat(string name, string desc, string def, double min, double max, double step, int order, T2IParamGroup group = null)
    {
        return T2IParamTypes.Register<double>(new($"[Capitan] {name}", desc, def, Min: min, Max: max, Step: step, Group: group ?? CapitanGroup, FeatureFlag: "capitan_enhancer", OrderPriority: order, ViewType: ParamViewType.SLIDER));
    }

    private static T2IRegisteredParam<bool> RegisterBool(string name, string desc, bool def, int order, T2IParamGroup group = null)
    {
        return T2IParamTypes.Register<bool>(new($"[Capitan] {name}", desc, def.ToString().ToLower(), Group: group ?? CapitanGroup, FeatureFlag: "capitan_enhancer", OrderPriority: order));
    }

    private static T2IRegisteredParam<int> RegisterInt(string name, string desc, int def, int min, int max, int order, T2IParamGroup group = null)
    {
        return T2IParamTypes.Register<int>(new($"[Capitan] {name}", desc, def.ToString(), Min: min, Max: max, Group: group ?? CapitanGroup, FeatureFlag: "capitan_enhancer", OrderPriority: order));
    }

    private static T2IRegisteredParam<string> RegisterDevice(string name, string desc, int order, T2IParamGroup group = null)
    {
        return T2IParamTypes.Register<string>(new($"[Capitan] {name}", desc, "auto", GetValues: (_) => ["auto", "cpu", "cuda:0", "cuda:1"], Group: group ?? CapitanGroup, FeatureFlag: "capitan_enhancer", OrderPriority: order));
    }
}