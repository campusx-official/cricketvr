using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CricketVR.Editor
{
    /// <summary>
    /// Builds a generated part of the scene in place, so a builder re-run with unchanged settings leaves the scene file
    /// unchanged: each object is found by name under its parent (created only when missing), keeps its object ID, and is
    /// put back in the order the builder creates it. Anything under the root the builder no longer creates is removed by
    /// <see cref="RemoveUnused"/>. Names must be unique among siblings.
    /// </summary>
    public sealed class GeneratedTree
    {
        readonly HashSet<Transform> _used = new HashSet<Transform>();
        readonly Dictionary<Transform, int> _nextIndex = new Dictionary<Transform, int>();

        public GameObject Root { get; }
        /// <summary>True when the root did not exist before this build.</summary>
        public bool CreatedRoot { get; }

        public GeneratedTree(Transform parent, string name, params Type[] components)
        {
            CreatedRoot = parent.Find(name) == null;
            // The root keeps its place among the parent's other (hand-built) children; a new one goes last.
            Root = Get(parent, name, components, ordered: false);
        }

        /// <summary>The named child of <paramref name="parent"/>, active, with the given components, at the next sibling index.</summary>
        public GameObject Child(Transform parent, string name, params Type[] components) => Get(parent, name, components, ordered: true);

        GameObject Get(Transform parent, string name, Type[] components, bool ordered)
        {
            var t = parent.Find(name);
            GameObject go;
            if (t == null)
            {
                go = new GameObject(name, components);
                go.transform.SetParent(parent, false);
                t = go.transform;
            }
            else
            {
                go = t.gameObject;
                foreach (var type in components)
                    if (!go.TryGetComponent(type, out _))
                        go.AddComponent(type);
                go.SetActive(true);
            }
            if (!_used.Add(t))
                throw new InvalidOperationException($"Generated object {name} under {parent.name} is created twice; sibling names must be unique.");
            if (!ordered)
                return go;
            _nextIndex.TryGetValue(parent, out int index);
            t.SetSiblingIndex(index);
            _nextIndex[parent] = index + 1;
            return go;
        }

        public GameObject Child(GameObject parent, string name, params Type[] components) => Child(parent.transform, name, components);

        /// <summary>Removes every object under the root that this build did not create or reuse.</summary>
        public void RemoveUnused()
        {
            var stale = new List<GameObject>();
            Collect(Root.transform, stale);
            foreach (var go in stale)
                Object.DestroyImmediate(go);
        }

        void Collect(Transform t, List<GameObject> stale)
        {
            foreach (Transform child in t)
            {
                if (_used.Contains(child)) Collect(child, stale);
                else stale.Add(child.gameObject);
            }
        }
    }
}
