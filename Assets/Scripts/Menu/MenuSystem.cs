using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using System;

public class MenuSystem : MonoBehaviour
{
     public void Play()
    {
       SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex + 1);
    }


    public void Credits ()
    {
        SceneManager.LoadScene("Credits");
    }

    public void Controlls ()
    {
        SceneManager.LoadScene("Controlls");
    }

    public void Exit()
    {
        
        
        Debug.Log("Saliendo del juegoo....");
        Application.Quit();

    }
    public void Regresar ()
    {
        if (TransitionManager.Instance != null)
        Destroy(TransitionManager.Instance.gameObject);
        SceneManager.LoadScene("Menu");
    }
}
