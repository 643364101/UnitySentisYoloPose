using UnityEngine;

public class Singleton<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static readonly object _locker = new object();

    static Singleton()
    {
    }

    public static T Instance
    {
        get
        {
            if (_instance == null)
            {
                lock (_locker)
                {
                    if (_instance == null)
                    {
                        _instance = FindObjectOfType<T>();
                        if (_instance == null)
                        {
                            GameObject obj = new GameObject(typeof(T).ToString());
                            _instance = obj.AddComponent<T>();
                        }
                    }
                }
            }

            return _instance;
        }
    }
}