using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class TSettings : MonoBehaviour
{

    public TRenderer tr;
    public GameObject tickBar;

    public Dropdown dropdown;

    // Start is called before the first frame update
    void Start()
    {
        Resolution[] resolutions = Screen.resolutions;

        List<string> m_DropOptions = new List<string> {};
        // Print the resolutions
        foreach (var res in resolutions)
        {
            m_DropOptions.Add(res.width + "x" + res.height + " : " + res.refreshRate);
        }
 
        //Clear the old options of the Dropdown menu
        dropdown.ClearOptions();
        //Add the options created in the List above
        dropdown.AddOptions(m_DropOptions);
    }

    public void Open()
    {
        gameObject.SetActive(true);
    }

    public void Close()
    {
        gameObject.SetActive(false);
    }

    public void showTickBar()
    {
        tickBar.SetActive(!tickBar.activeSelf);
    }

    public void toggleFullScreen()
    {
        Screen.fullScreen = !Screen.fullScreen;
    }

    public void changeResTo(System.Int32 idx)
    {
        Screen.SetResolution(Screen.resolutions[idx].width, Screen.resolutions[idx].height, Screen.fullScreen);
    }

    public void Exit()
    {
        Application.Quit();
    }
}
