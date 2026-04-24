using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Controlador principal de jugador en Primera Persona.
/// Gestiona movimiento estilo First-Person-Shooter (Módulo de Operario) usando CharacterController
/// e integra de forma obligatoria el nuevo Unity Input System (v1.1+).
/// </summary>
[RequireComponent(typeof(CharacterController))]
public class FirstPersonOperative : MonoBehaviour
{
    [Header("Referencias de Jerarquía")]
    [Tooltip("El Transform de la cámara principal. Debería ser un objeto 'hijo' físico de este jugador a la altura de sus ojos.")]
    [SerializeField] private Transform _cameraTransform;

    [Header("Ajustes de Velocidad")]
    [Tooltip("Velocidad de caminata en metros por segundo.")]
    [SerializeField] private float _moveSpeed = 5f;
    
    [Tooltip("Aceleración de gravedad. Comúnmente -9.81 en planeta Tierra.")]
    [SerializeField] private float _gravity = -9.81f;

    [Header("Ajustes del Visor (Mouse Look)")]
    [Tooltip("Sensibilidad al rotar la cabeza y la cintura con el ratón físico.")]
    [SerializeField] private float _mouseSensitivity = 15f;
    
    [Tooltip("Rango de bloqueo en grados de la rotación vertical, para evitar dislocar el cuello (Dar giros de 360 grados).")]
    [SerializeField] private float _maxLookAngle = 80f;

    // Referencias internas
    private CharacterController _characterController;
    private float _xRotation = 0f;          // Acumulador de ángulo vertical Pitch de la cabeza
    private float _verticalVelocity = 0f;   // Manejo de Gravedad matemática paralela

    private void Awake()
    {
        // 1. Obtiene de manera forzada el componente CharacterController al iniciar para evitar referencias perdidas.
        _characterController = GetComponent<CharacterController>();

        // 2. Prepara la interfaz para simulación bloqueando el puntero del Windows/Mac en el centro de la pantalla.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // Comprobación de seguridad
        if (_cameraTransform == null)
        {
            Debug.LogError("FirstPersonOperative: ¡Falta asignar el Cámara Transform en el inspector!", this);
        }
    }

    private void Update()
    {
        // El orden es importante: la mira antes del desplazamiento previene desajustes de física por cuadros.
        HandleMouseLook();
        HandleMovement();
    }

    /// <summary>
    /// Gestiona la rotación de la espina del jugador en el Eje Y y la rotación del cuello (Cámara) en el eje X.
    /// </summary>
    private void HandleMouseLook()
    {
        // Abortar si no contamos con Input Hardware válido de Mouse o no hay cámara asignada.
        if (Mouse.current == null || _cameraTransform == null) return;

        // .delta.ReadValue() extrae en bruto la variación de píxeles trazados en el MousePad desde el último cuadro.
        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        // Multiplicamos por Sensitivity y limitamos su disparidad de fotogramas usando deltaTime para hacerlo fluido sin importar el Lag.
        float mouseX = mouseDelta.x * _mouseSensitivity * Time.deltaTime;
        float mouseY = mouseDelta.y * _mouseSensitivity * Time.deltaTime;

        // --- Rotación Vertical (Cabeza) ---
        // Se RESTA el eje Y en Unity para que subir el mouse mire al cielo. (Si sumas, los controles "Mouse Look" se invierten como en avión)
        _xRotation -= mouseY;
        // Limitamos para no crujirle el cuello al operario pasándose de rosca y viendo hacia su propio torso.
        _xRotation = Mathf.Clamp(_xRotation, -_maxLookAngle, _maxLookAngle);
        
        // Aplicamos EulerAngles a Transform Local de la cámara (Pitch, Yaw, Roll).
        _cameraTransform.localRotation = Quaternion.Euler(_xRotation, 0f, 0f);

        // --- Rotación Horizontal (Torso Inferior) ---
        // Toda la cápsula o chasis debe rotar en el eje Y hacia la izquierda o derecha para cambiar el frente.
        transform.Rotate(Vector3.up * mouseX);
    }

    /// <summary>
    /// Procesa transiciones espaciales en la cuadrícula de movimiento con colisiones WASD + Gravedad Integrada.
    /// </summary>
    private void HandleMovement()
    {
        // Abortar si retiraron el teclado en runtime.
        if (Keyboard.current == null) return;

        // Definimos las intenciones del usuario consultando teclas duras nativamente
        float moveX = 0f;
        float moveZ = 0f;

        // Escucha directa hacia los interruptores del teclado
        if (Keyboard.current.wKey.isPressed) moveZ += 1f;   // Frente
        if (Keyboard.current.sKey.isPressed) moveZ -= 1f;   // Atrás
        if (Keyboard.current.aKey.isPressed) moveX -= 1f;   // Izquierda
        if (Keyboard.current.dKey.isPressed) moveX += 1f;   // Derecha

        // Agrupamos el vector resultante. ".normalized" soluciona que el típico WASD camine a velocidad x1.41 en diagonal.
        Vector3 inputDirection = new Vector3(moveX, 0f, moveZ).normalized;

        // Convertimos un frente Absoluto (World Space) a un Relativo (Local Space). "Si apreto la W camino hacia DONDE VEA MI PECHO".
        Vector3 move = transform.right * inputDirection.x + transform.forward * inputDirection.z;

        // --- Simulación de Gravedad de Terrestre ---
        // Checamos si la esfera inferior de colisión toca el suelo para resetear nuestro Vector cayendo a tierra.
        if (_characterController.isGrounded && _verticalVelocity < 0)
        {
            // Usamos -2f en lugar de 0 para forzarlo a pegarse y morder a rampas bajantes previniendo "Bouncing".
            _verticalVelocity = -2f;
        }

        // Aplicamos la caída libre y acumulación constante a nuestra var de velocidad V. (V = V0 + At)
        _verticalVelocity += _gravity * Time.deltaTime;

        // --- Ejecución ---
        // Ensamblamos la velocidad XZ por la rapidez final, añadiéndole la constante vertical Y.
        Vector3 finalVelocity = (move * _moveSpeed) + (Vector3.up * _verticalVelocity);

        // Envolvemos todo en DeltaTime de nuevo porque Move() procesa desplazamiento literal como Velocidad * Tiempo
        _characterController.Move(finalVelocity * Time.deltaTime);
    }
}
