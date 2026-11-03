using UnityEngine;

namespace LMDDrone.Game;

/// <summary>What a flight needs to know of the level, looked up once per level: the search walks every object.</summary>
internal static class Level
{
    /// <summary>
    /// The height of the mountain's finish camp, or the fallback in a level without one. A level holds the finish lines of all its trails, off but for the one ridden: they all come
    /// into the same camp, within a metre, so their middle is one height for all of them, and each fifth trail has its own. Found under the level's settings, so it costs no search.
    /// </summary>
    public static float CampHeight(float fallback)
    {
        var settings = GameObject.Find("MountainSettings");
        if (settings == null)
        {
            return fallback;
        }

        var sum = 0f;
        var count = 0;
        for (var i = 0; i < settings.transform.childCount; i++)
        {
            var finish = settings.transform.GetChild(i).Find("GameObject_FinishLine");
            if (finish != null)
            {
                sum += finish.position.y;
                count++;
            }
        }

        return count > 0 ? sum / count : fallback;
    }

    /// <summary>The box of all terrain chunks, whichever detail they're at.</summary>
    public static Bounds FindTerrain(Vector3 fallback)
    {
        var box = new Bounds(fallback, Vector3.one * 200f);
        var first = true;
        foreach (var filter in Resources.FindObjectsOfTypeAll<MeshFilter>())
        {
            if (filter.name != "terrain_chunk" || !filter.gameObject.scene.IsValid())
            {
                continue;
            }

            var renderer = filter.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                continue;
            }

            if (first)
            {
                box = renderer.bounds;
                first = false;
            }
            else
            {
                box.Encapsulate(renderer.bounds);
            }
        }

        return box;
    }
}
