using UnityEngine;

/// <summary>
/// Script diseñado para simulaciones de excavación en Unity 6.x.
/// Permite deformar el terreno restando altura en un área circular de manera altamente optimizada.
/// </summary>
public class TerrainDeformer : MonoBehaviour
{
    [Header("Configuración Principal")]
    [Tooltip("Referencia al componente de Terreno que se va a deformar. Si se deja vacío, intentará buscar uno en este mismo GameObject al iniciar.")]
    [SerializeField] private Terrain _terrain;

    [Tooltip("Límite de suelo duro (Altura local mínima permitida). Previene excavar por debajo de un punto arbitrario y evita bajar al infinito. 0 es la base del modelo.")]
    [SerializeField] private float _minHeightFloor = 0f;

    [Header("Valores Ajustables de Pala (Valores por defecto)")]
    [Tooltip("Radio de la pala u hoyo a excavar medido en metros.")]
    [SerializeField] private float _radioPala = 2f;

    [Tooltip("Profundidad restada por cada 'palada' o fotograma, medida en metros.")]
    [SerializeField] private float _profundidadPorPalada = 0.5f;

    // Trackeamos si hay deformaciones en memoria pendientes de renderizar
    private bool _hasPendingChanges = false;

    private void Awake()
    {
        // En caso de que se nos olvide arrastrar la referencia en el inspector
        if (_terrain == null)
        {
            _terrain = GetComponent<Terrain>();
            if (_terrain == null)
            {
                Debug.LogError("TerrainDeformer: No se asignó ningún terreno y no se encontró el componente Terrain adjunto.", this);
            }
        }
    }

    /// <summary>
    /// Método de utilidad si se desea invocar la excavación sin parámetros desde eventos de UI, usando los valores del inspector.
    /// </summary>
    public void DigWithDefaults(Vector3 worldPosition)
    {
        Dig(worldPosition, _radioPala, _profundidadPorPalada);
    }

    /// <summary>
    /// Función principal que convierte coordenadas de mundo al Heightmap de Unity, y performa la geometría.
    /// Modifica los datos con baja latencia sin recalcular la malla gráfica inmediatamente.
    /// </summary>
    /// <param name="worldPosition">Posición 3D del mundo en donde impactó la pala.</param>
    /// <param name="radius">Área o radio del agujero a generar (en metros).</param>
    /// <param name="depth">Magnitud de altura a sustraer del terreno (en metros).</param>
    public void Dig(Vector3 worldPosition, float radius, float depth)
    {
        if (_terrain == null || _terrain.terrainData == null) return;

        TerrainData tData = _terrain.terrainData;

        // 1. Convertir la posición global (mundo) a coordenadas locales del terreno
        // Restamos la posición global base del Terrain al impacto para conseguir un vector "offset"
        Vector3 localPos = worldPosition - _terrain.transform.position;

        // 2. Normalizar de Local a Coordenadas UV/Textura (0.0 a 1.0)
        // Dividimos la posición entre las dimensiones totales del Terrain para saber el % recorrido
        float normalizedX = localPos.x / tData.size.x;
        float normalizedZ = localPos.z / tData.size.z;

        // 3. Obtener las coordenadas del Heightmap (índices en la matriz) multiplicando el % 
        // por la resolución del mismo (número de vértices por lado, ej. 512).
        int widthRes = tData.heightmapResolution;
        int heightRes = tData.heightmapResolution; // Aunque es cuádruple, declaramos z alias height.
        
        int heightmapX = Mathf.RoundToInt(normalizedX * (widthRes - 1));
        int heightmapZ = Mathf.RoundToInt(normalizedZ * (heightRes - 1));

        // 4. Calcular los vértices involucrados según el "radius"
        // Transformamos el radio deseado a pixeles/celdas de heightmap mediante una regla de tres:
        int radiusInCellsX = Mathf.RoundToInt(radius * ((widthRes - 1) / tData.size.x));
        int radiusInCellsZ = Mathf.RoundToInt(radius * ((heightRes - 1) / tData.size.z));

        // Clampeamos para asegurar no tratar de leer o escribir fuera de la memoria del Array (Array out of Bounds)
        int startX = Mathf.Clamp(heightmapX - radiusInCellsX, 0, widthRes - 1);
        int startZ = Mathf.Clamp(heightmapZ - radiusInCellsZ, 0, heightRes - 1);
        int endX = Mathf.Clamp(heightmapX + radiusInCellsX + 1, 0, widthRes - 1);
        int endZ = Mathf.Clamp(heightmapZ + radiusInCellsZ + 1, 0, heightRes - 1);

        int rectWidth = endX - startX;
        int rectLength = endZ - startZ;

        // Validamos protección contra cálculos de rectángulos de 0 polígonos
        if (rectWidth <= 0 || rectLength <= 0) return;

        // 5. Extraer fragmento del Heightmap
        // En vez de tener en memoria un mapa de 4000x4000 flotantes, tomamos única y exclusivamente el bloque del radio.
        // Array de formato [z, x] entre 0.0 y 1.0 (que representan la altura máxima size.y).
        float[,] heights = tData.GetHeights(startX, startZ, rectWidth, rectLength);

        // Transformamos parámetros de mundo a valores de altura normalizada del motor (0.0 a 1.0)
        float normalizedDepth = depth / tData.size.y;
        float normalizedMinHeightLimit = _minHeightFloor / tData.size.y;

        // Utilizamos radio cuadrado para optimizar matemáticamente y no usar la costosa función "Mathf.Sqrt" (raíz cuadrada).
        float targetSqrRadius = radius * radius;

        // 6. Loop de esculpido: Manipulamos vértices usando distancia euclidiana para lograr un cráter esférico perfecto
        for (int z = 0; z < rectLength; z++)
        {
            for (int x = 0; x < rectWidth; x++)
            {
                int currentX = startX + x;
                int currentZ = startZ + z;

                // Re-calculamos dónde queda en nuestro mundo la celda sobre la que estamos iterando:
                float pointLocalX = ((float)currentX / (widthRes - 1)) * tData.size.x;
                float pointLocalZ = ((float)currentZ / (heightRes - 1)) * tData.size.z;

                // Aplicamos Pitágoras para hallar el radio de distancia en metros del punto iterado comparado al localPos
                float distSqr = (pointLocalX - localPos.x) * (pointLocalX - localPos.x) + 
                                (pointLocalZ - localPos.z) * (pointLocalZ - localPos.z);
                                
                // Verificar si esta fracción de suelo está adentro de la brocha circular de nuestro impacto.
                if (distSqr <= targetSqrRadius)
                {
                    // Restar altura al punto
                    float currentH = heights[z, x];
                    float newH = currentH - normalizedDepth;

                    // 7. Limitador del suelo base (Hard limit constraint)
                    if (newH < normalizedMinHeightLimit)
                    {
                        newH = normalizedMinHeightLimit;
                    }
                    
                    heights[z, x] = newH;
                }
            }
        }

        // 8. Aplicación aplazada para máximo rendimiento (OPTIMIZACIÓN CLAVE)
        // Enlazar SetHeightsDelayLOD modifica la topología bruta omitiendo recalcular colisiones e instanciamientos visuales (MipMaps),
        // eliminando los severos crasheos o caídas de FPS que sufren los desarrolladores en equipos de gama baja-media.
        tData.SetHeightsDelayLOD(startX, startZ, heights);
        _hasPendingChanges = true;
    }

    /// <summary>
    /// Fusiona los cambios aplazados impactando ahora sí a los Niveles de Detalle (LOD) de manera distribuida.
    /// Recomendación de experto: No llames este método a cada frame desde el Update.
    /// Llámalo en un Timer cada 0.2 segundos, o cuando la máquina retroexcavadora haya soltado de apretar el gatillo (Input Up).
    /// </summary>
    public void ApplyDelayedChanges()
    {
        if (_hasPendingChanges && _terrain != null && _terrain.terrainData != null)
        {
            // Sincroniza la geometría demorada y actualiza los Mesh Colliders subyacentes sin causar cuello de botella.
            _terrain.terrainData.SyncHeightmap();
            _hasPendingChanges = false;
        }
    }

    /// <summary>
    /// Salvavidas para evitar glitches de memoria y forzar la reescritura al mapa precompilado
    /// si el script o GameObject se llegaran a desactivar.
    /// </summary>
    private void OnDisable()
    {
        ApplyDelayedChanges();
    }
}
