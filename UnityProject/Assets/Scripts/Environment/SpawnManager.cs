// UnityProject/Assets/Scripts/Environment/SpawnManager.cs
using UnityEngine;

namespace HormuzAI.Environment
{
    /// <summary>
    /// SpawnPoints 오브젝트의 자식 Transform 을 스폰 위치로 관리한다.
    /// Awake 에서 자식을 자동 수집하므로 Inspector 할당 불필요.
    /// 멀티 에이전트 레이싱을 위해 인덱스 순환을 지원한다.
    /// </summary>
    public class SpawnManager : MonoBehaviour
    {
        private Transform[] _spawnPoints;

        private void Awake()
        {
            _spawnPoints = new Transform[transform.childCount];
            for (int i = 0; i < transform.childCount; i++)
                _spawnPoints[i] = transform.GetChild(i);
        }

        /// <summary>index 번째 스폰 포인트를 반환한다 (범위 초과 시 순환).</summary>
        public Transform GetSpawnPoint(int index = 0)
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0)
                return transform;
            return _spawnPoints[index % _spawnPoints.Length];
        }

        /// <summary>등록된 스폰 포인트 수.</summary>
        public int SpawnCount => _spawnPoints?.Length ?? 0;
    }
}
