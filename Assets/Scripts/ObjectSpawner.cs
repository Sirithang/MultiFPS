using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

public class ObjectSpawner : NetworkBehaviour
{
    public GameObject enemyPrefab;
    public int enemyToSpawn = 5;


    public override void OnStartServer()
    {
        for (int i = 0; i < enemyToSpawn; i++)
        {
            var spawnPosition = new Vector3(
                Random.Range(-8.0f, 8.0f),
                0.0f,
                Random.Range(-8.0f, 8.0f));

            var spawnRotation = Quaternion.Euler(
                0.0f,
                Random.Range(0, 180),
                0.0f);

            var enemy = (GameObject)Instantiate(enemyPrefab, transform.position + spawnPosition, spawnRotation);
            NetworkServer.Spawn(enemy);
        }
    }
}
