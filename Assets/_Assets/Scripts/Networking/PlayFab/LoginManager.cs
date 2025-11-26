using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LoginManager : MonoBehaviour
{
    public GameObject loginPanel;
    public GameObject signUp;

    public void SignUpOption()
    {
        loginPanel.SetActive(false);
        signUp.SetActive(true);
    }

    public void CancelLogin()
    {
        loginPanel.SetActive(true);
        signUp.SetActive(false);
    }

    public void Login()
    {
        Debug.Log("Attempting Login...");
    }

    public void SignUp()
    {
        Debug.Log("Attempting Sign Up...");
    }

    public void ForgotPassword()
    {
        Debug.Log("Attempting Account Recovery..");
    }
}
