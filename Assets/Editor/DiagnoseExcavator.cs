using UnityEngine;
using UnityEditor;

public class DiagnoseExcavator
{
    [MenuItem("Tools/Diagnose Excavator Animator")]
    public static void Diagnose()
    {
        var go = GameObject.Find("SM_Veh_Excavator_01_Top");
        if (go == null) { Debug.Log("NO ENCONTRADO EN ESCENA"); return; }

        var animators = go.GetComponents<Animator>();
        Debug.Log($"[Excavator] Animators encontrados: {animators.Length}");
        for (int i = 0; i < animators.Length; i++)
        {
            var a = animators[i];
            string ctrl = a.runtimeAnimatorController != null ? a.runtimeAnimatorController.name : "NULL";
            Debug.Log($"[Excavator] [{i}] Controller={ctrl}, Enabled={a.enabled}, Culling={a.cullingMode}");
        }

        // También verificar si hay override de escena
        var prefabType = PrefabUtility.GetPrefabInstanceStatus(go);
        Debug.Log($"[Excavator] PrefabStatus: {prefabType}");
        var mods = PrefabUtility.GetObjectOverrides(go.transform.root.gameObject);
        Debug.Log($"[Excavator] Overrides en root: {mods.Count}");
        foreach (var mod in mods)
        {
            Debug.Log($"[Excavator] Override: {mod.instanceObject}");
        }
    }
}
