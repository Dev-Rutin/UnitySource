using System.Collections.Generic;
using UnityEngine;

namespace Rutin.GameFramework.Core
{
    /// <summary>
    /// Composition root for gameplay objects. It owns feature lifecycle without
    /// requiring each feature to run an independent Update loop.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameplayEntity : MonoBehaviour
    {
        private readonly List<EntityFeature> _features = new(8);
        private readonly List<EntityFeature> _discoveryBuffer = new(8);
        private bool _entityActive;
        private bool _isShuttingDown;

        public int FeatureCount => _features.Count;

        private void Awake()
        {
            _discoveryBuffer.Clear();
            GetComponents(_discoveryBuffer);

            for (int i = 0; i < _discoveryBuffer.Count; i++)
            {
                RegisterFeature(_discoveryBuffer[i]);
            }

            _discoveryBuffer.Clear();
        }

        private void OnEnable()
        {
            _entityActive = true;
            for (int i = 0; i < _features.Count; i++)
            {
                EntityFeature feature = _features[i];
                if (feature != null && feature.isActiveAndEnabled)
                {
                    feature.SetFeatureActive(true);
                }
            }
        }

        private void OnDisable()
        {
            _entityActive = false;
            for (int i = _features.Count - 1; i >= 0; i--)
            {
                _features[i]?.SetFeatureActive(false);
            }
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            for (int i = _features.Count - 1; i >= 0; i--)
            {
                _features[i]?.Unbind();
            }

            _features.Clear();
        }

        public bool TryGetFeature<TFeature>(out TFeature feature)
            where TFeature : EntityFeature
        {
            for (int i = 0; i < _features.Count; i++)
            {
                if (_features[i] is TFeature typedFeature)
                {
                    feature = typedFeature;
                    return true;
                }
            }

            feature = null;
            return false;
        }

        internal void RegisterFeature(EntityFeature feature)
        {
            if (feature == null || _isShuttingDown || ContainsReference(feature))
            {
                return;
            }

            feature.Bind(this);

            int insertIndex = _features.Count;
            int order = feature.InitializationOrder;
            for (int i = 0; i < _features.Count; i++)
            {
                if (_features[i].InitializationOrder > order)
                {
                    insertIndex = i;
                    break;
                }
            }

            _features.Insert(insertIndex, feature);

            if (_entityActive && feature.isActiveAndEnabled)
            {
                feature.SetFeatureActive(true);
            }
        }

        internal void UnregisterFeature(EntityFeature feature)
        {
            if (feature == null || _isShuttingDown)
            {
                return;
            }

            int index = IndexOfReference(feature);
            if (index < 0)
            {
                return;
            }

            feature.Unbind();
            _features.RemoveAt(index);
        }

        internal void NotifyFeatureEnabled(EntityFeature feature)
        {
            if (!_isShuttingDown && _entityActive && ContainsReference(feature))
            {
                feature.SetFeatureActive(true);
            }
        }

        internal void NotifyFeatureDisabled(EntityFeature feature)
        {
            if (!_isShuttingDown && ContainsReference(feature))
            {
                feature.SetFeatureActive(false);
            }
        }

        private bool ContainsReference(EntityFeature feature)
        {
            return IndexOfReference(feature) >= 0;
        }

        private int IndexOfReference(EntityFeature feature)
        {
            for (int i = 0; i < _features.Count; i++)
            {
                if (ReferenceEquals(_features[i], feature))
                {
                    return i;
                }
            }

            return -1;
        }
    }
}
