using System.Collections.Generic;
using UnityEngine;

namespace Rutin.GameFramework.Core
{
    /// <summary>
    /// Composition root for gameplay objects. It owns feature lifecycle without
    /// requiring each feature to run an independent Update loop.
    /// </summary>
    [DefaultExecutionOrder(-9000)]
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
                EntityFeature feature = _discoveryBuffer[i];
                if (!feature.HasInitializationFailed && !ContainsReference(feature))
                {
                    InsertFeatureSorted(feature);
                }
            }

            for (int i = 0; i < _features.Count;)
            {
                if (TryBindFeature(_features[i]))
                {
                    i++;
                    continue;
                }

                _features.RemoveAt(i);
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
                    TrySetFeatureActive(feature, true);
                }
            }
        }

        private void OnDisable()
        {
            _entityActive = false;
            for (int i = _features.Count - 1; i >= 0; i--)
            {
                EntityFeature feature = _features[i];
                if (feature != null)
                {
                    TrySetFeatureActive(feature, false);
                }
            }
        }

        private void OnDestroy()
        {
            _isShuttingDown = true;
            for (int i = _features.Count - 1; i >= 0; i--)
            {
                TryUnbindFeature(_features[i]);
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
            if (feature == null ||
                feature.HasInitializationFailed ||
                _isShuttingDown ||
                ContainsReference(feature))
            {
                return;
            }

            InsertFeatureSorted(feature);
            if (!TryBindFeature(feature))
            {
                _features.Remove(feature);
                return;
            }

            if (_entityActive && feature.isActiveAndEnabled)
            {
                TrySetFeatureActive(feature, true);
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

            try
            {
                TryUnbindFeature(feature);
            }
            finally
            {
                _features.RemoveAt(index);
            }
        }

        internal void NotifyFeatureEnabled(EntityFeature feature)
        {
            if (!_isShuttingDown && _entityActive && ContainsReference(feature))
            {
                TrySetFeatureActive(feature, true);
            }
        }

        internal void NotifyFeatureDisabled(EntityFeature feature)
        {
            if (!_isShuttingDown && ContainsReference(feature))
            {
                TrySetFeatureActive(feature, false);
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

        private void InsertFeatureSorted(EntityFeature feature)
        {
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
        }

        private bool TryBindFeature(EntityFeature feature)
        {
            try
            {
                feature.Bind(this);
                return true;
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, feature);
                return false;
            }
        }

        private static void TryUnbindFeature(EntityFeature feature)
        {
            if (feature == null)
            {
                return;
            }

            try
            {
                feature.Unbind();
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, feature);
            }
        }

        private static void TrySetFeatureActive(EntityFeature feature, bool active)
        {
            try
            {
                feature.SetFeatureActive(active);
            }
            catch (System.Exception exception)
            {
                Debug.LogException(exception, feature);
            }
        }
    }
}
