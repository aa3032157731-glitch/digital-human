using UnityEngine;

public class ClickCharacter : MonoBehaviour
{
    public Animator animator;

    void Update()
    {
        if (Input.GetMouseButtonDown(0))
        {
            Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition);

            if (Physics.Raycast(ray, out RaycastHit hit))
            {
                if (hit.transform == transform || hit.transform.IsChildOf(transform))
                {
                    PlayRandomWave();
                }
            }
        }
    }

    void PlayRandomWave()
    {
        int randomIndex = Random.Range(0, 3); // 只会随机出 0 或 1

        if (randomIndex == 0)
        {
            animator.SetTrigger("Wave");
        }
        else if (randomIndex == 1)
        {
            animator.SetTrigger("Angry");
        }
        else
        {
            animator.SetTrigger("Bow");
        }
    }
}