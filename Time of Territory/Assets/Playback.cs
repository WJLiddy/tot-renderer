using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Playback : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        
    }

    public void Pause()
    {
        Time.timeScale = 0f;
    }

    public void Play()
    {
        Time.timeScale = 1f;
    }

    public void FF()
    {
        Time.timeScale = 10f;
    }
}
