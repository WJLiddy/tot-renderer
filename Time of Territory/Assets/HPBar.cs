using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class HPBar : MonoBehaviour
{
    public GameObject front;
    public GameObject back;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        this.transform.forward = Camera.main.transform.forward;
    }

    public void setHPBar(int currHP, int maxHP)
    {
       if(currHP == maxHP)
       {
            front.SetActive(false);
            back.SetActive(false);
            return;
       }

       if(currHP <= 0)
       {
            front.SetActive(false);
            return;
       }

        front.SetActive(true);
        back.SetActive(true);


        front.transform.localScale = new Vector3((float)currHP / (float)maxHP, 0.2f, 1);
    }
}
