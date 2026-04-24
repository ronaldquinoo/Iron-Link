using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Script de prueba para validar el sistema de excavación basado en cámara y clics del ratón.
/// Pensado como un esqueleto para interacciones de Gameplay, utilizando buenas prácticas
/// de latencia aplazada con el sistema de terrenos y compatibilidad con el nuevo Input System de Unity.
/// </summary>
public class TerrainInteractionTest : MonoBehaviour
{
    [Header("Referencias")]
    [Tooltip("El sistema central de deformación creado previamente.")]
    [SerializeField] private TerrainDeformer _terrainDeformer;

    [Header("Parámetros de Excavación")]
    [Tooltip("El radio en metros de la brocha a utilizar mientras presionas click.")]
    [SerializeField] private float _digRadius = 3f;

    [Tooltip("La cantidad de metros a bajar por CADA FRAME que el click esté presionado. Valor muy bajo recomendado.")]
    [SerializeField] private float _digDepthPerFrame = 0.1f;

    // Referencia cacheada de la cámara para no llamar a "Camera.main" excesivamente, 
    // aunque en versiones modernas de Unity "Camera.main" ya está bastante optimizado.
    private Camera _mainCamera;

    private void Start()
    {
        _mainCamera = Camera.main;
        
        if (_terrainDeformer == null)
        {
            Debug.LogWarning("TerrainInteractionTest: ¡No olvidaste asignar el script de TerrainDeformer en el inspector!", this);
        }
    }

    private void Update()
    {
        // Si no hay cámara principal, no hay deformador instanciado, o no se detecta el mouse físico en el sistema, salimos.
        if (_mainCamera == null || _terrainDeformer == null || Mouse.current == null) return;

        // --- CÓDIGO DE INTERACCIÓN SOSTENIDA (NUEVO INPUT SYSTEM) ---
        // isPressed retorna verdadero CADA FRAME mientras el usuario mantenga el Click Izquierdo sostenido.
        if (Mouse.current.leftButton.isPressed)
        {
            // 1. Trazado del Rayo
            // Obtenemos la posición 2D de la pantalla directamente usando el Input System con "Mouse.current.position.ReadValue()".
            // Y de ahí lanzamos un rayo invisible desde la cámara global al mundo.
            Ray impactRay = _mainCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

            // 2. Colisión Física
            // Validamos si nuestro rayo choca con un colisionador (en este caso idealmente la malla de colisión del Terreno).
            if (Physics.Raycast(impactRay, out RaycastHit hitInfo))
            {
                // Obtenemos las coordenadas planetarias exactas de la colisión del rayo.
                Vector3 surfaceHitPoint = hitInfo.point;

                // 3. Ejecutamos la excavación silenciosa
                // Nota: Gracias al diseño previo de TerrainDeformer, esta función NO actualiza la geometría visible al instante.
                // Mutar la topología cruda a esta frecuencia de cuadros es rápido. Si la actualizáramos visualmente cada vez, 
                // el Thread principal pausaría el CPU provocando stutters en los FPS. 
                _terrainDeformer.Dig(surfaceHitPoint, _digRadius, _digDepthPerFrame);
            }
        }

        // --- CÓDIGO DE CIERRE (RENDIMIENTO CRUCIAL CON EL NUEVO INPUT) ---
        // wasReleasedThisFrame ocurre exactamente 1 único Frame al momento de soltar el click izquierdo físico.
        if (Mouse.current.leftButton.wasReleasedThisFrame)
        {
            // 4. Refresco Visual (SyncHeghtmap)
            // Ya que el usuario terminó de operar la espátula o pala excavatoria, le pedimos a Unity
            // que ahora sí envíe el arreglo modificado a la Gráfica y regenere los aceleradores físicos en colisiones.
            // Hacer esto exclusivamente en la liberación del botón previene bloqueos catastróficos del motor.
            _terrainDeformer.ApplyDelayedChanges();
        }
    }
}
