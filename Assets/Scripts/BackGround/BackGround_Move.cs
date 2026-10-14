using UnityEngine;
using System.Collections;
using System.Collections.Generic;


public class BackGround_Move: MonoBehaviour
{
    [SerializeField] private Vector2 velocidad_fondo;

    private Vector2 offset;

    private Material material;

     private void Awake() {
        material = GetComponent<SpriteRenderer>().material;

    
    }

     private void Update() {
        offset = velocidad_fondo * Time.deltaTime;
        material.mainTextureOffset += offset;
    }
}

