using System.Collections.Generic;
using UnityEngine;

namespace GiantCity
{
    /// <summary>
    /// Procedurally generates a low-poly miniature city (buildings, lamps, and a ground) with colliders and
    /// a <see cref="CityBuilding"/> marker on every obstacle. The whole city is placed under a parent that the
    /// <see cref="WorldScaler"/> scales down to match the giantess. Deterministic via a seed.
    ///
    /// All coordinates are in the small world (the world root's local space); after scaling they shrink so
    /// the tiny player is about one toe of the giantess.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityGenerator : MonoBehaviour
    {
        [Header("Layout")]
        [Tooltip("Random seed for a reproducible city.")]
        public int seed = 20260909;
        [Tooltip("Half-extent (small-world units) of the city area.")]
        public float halfSize = 90f;
        [Tooltip("Cell grid spacing; buildings occupy cells and the gaps between them are streets/alleyways.")]
        public float cellSize = 12f;
        [Tooltip("Minimum building height in small-world units.")]
        public float minBuildingHeight = 6f;
        [Tooltip("Maximum building height in small-world units.")]
        public float maxBuildingHeight = 34f;
        [Tooltip("Probability that a non-empty cell contains a building (else it stays open as a street).")]
        [Range(0f, 1f)] public float buildingDensity = 0.72f;
        [Tooltip("Chance that a street cell happens to host a lamp post instead of staying empty.")]
        [Range(0f, 1f)] public float lampChance = 0.25f;

        private readonly List<GameObject> _objects = new List<GameObject>();

        /// <summary>Clears all previously generated objects.</summary>
        public void Clear()
        {
            for (int i = _objects.Count - 1; i >= 0; --i)
            {
                if (_objects[i] != null)
                {
                    Destroy(_objects[i]);
                }
            }
            _objects.Clear();
        }

        /// <summary>Clears and regenerates the whole city.</summary>
        public void Build()
        {
            Clear();
            BuildGround();
            BuildBlocks();
        }

        private void BuildGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.SetParent(transform, false);
            ground.transform.localScale = new Vector3(halfSize * 0.2f, 1f, halfSize * 0.2f);
            ground.transform.localPosition = Vector3.zero;

            // The ground is a walkable floor, not an obstacle, so it has no CityBuilding marker.
            Renderer r = ground.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = MakeMaterial("Ground", new Color(0.18f, 0.18f, 0.22f, 1f));
            }
            _objects.Add(ground);
        }

        private void BuildBlocks()
        {
            System.Random rng = new System.Random(seed);
            int cells = Mathf.FloorToInt((halfSize * 2f) / cellSize);
            float center = (cells - 1) * cellSize * 0.5f;

            for (int x = 0; x < cells; ++x)
            {
                for (int z = 0; z < cells; ++z)
                {
                    float posX = x * cellSize - center;
                    float posZ = z * cellSize - center;

                    if (rng.NextDouble() > buildingDensity)
                    {
                        // Street cell: maybe a lamp post.
                        if (rng.NextDouble() < lampChance)
                        {
                            SpawnLamp(posX, posZ, rng);
                        }
                        continue;
                    }

                    SpawnBuilding(posX, posZ, rng);
                }
            }
        }

        private void SpawnBuilding(float x, float z, System.Random rng)
        {
            float width = cellSize * (0.5f + (float)rng.NextDouble() * 0.4f);
            float depth = cellSize * (0.5f + (float)rng.NextDouble() * 0.4f);
            float height = Mathf.Lerp(minBuildingHeight, maxBuildingHeight, (float)rng.NextDouble());

            GameObject building = GameObject.CreatePrimitive(PrimitiveType.Cube);
            building.name = "Building";
            building.transform.SetParent(transform, false);
            building.transform.localPosition = new Vector3(x, height * 0.5f, z);
            building.transform.localScale = new Vector3(width, height, depth);
            building.AddComponent<CityBuilding>();

            Renderer r = building.GetComponent<Renderer>();
            if (r != null)
            {
                r.material = MakeMaterial("Building", Color.HSVToRGB((float)rng.NextDouble(), 0.35f, 0.78f));
            }
            _objects.Add(building);
        }

        private void SpawnLamp(float x, float z, System.Random rng)
        {
            GameObject lamp = new GameObject("Lamp");

            GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pole.name = "Pole";
            pole.transform.SetParent(lamp.transform, false);
            pole.transform.localPosition = new Vector3(x, 2.2f, z);
            pole.transform.localScale = new Vector3(0.25f, 2.2f, 0.25f);
            pole.AddComponent<CityBuilding>();

            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            head.name = "Head";
            head.transform.SetParent(lamp.transform, false);
            head.transform.localPosition = new Vector3(x, 4.5f, z);
            head.transform.localScale = Vector3.one * 0.6f;

            lamp.transform.SetParent(transform, false);

            Renderer poleR = pole.GetComponent<Renderer>();
            if (poleR != null)
            {
                poleR.material = MakeMaterial("LampPole", new Color(0.3f, 0.32f, 0.36f, 1f));
            }
            Renderer headR = head.GetComponent<Renderer>();
            if (headR != null)
            {
                headR.material = MakeMaterial("LampHead", new Color(1f, 0.9f, 0.55f, 1f));
            }

            _objects.Add(lamp);
        }

        private static Material MakeMaterial(string name, Color color)
        {
            Shader shader = Shader.Find("Standard");
            if (shader == null)
            {
                return null;
            }
            Material material = new Material(shader);
            material.name = name;
            material.color = color;
            return material;
        }
    }
}
