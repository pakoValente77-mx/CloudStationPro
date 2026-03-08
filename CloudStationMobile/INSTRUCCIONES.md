# Cómo abrir el proyecto en Xcode 🚀

Dado que Xcode utiliza un formato de archivo binario para sus proyectos (`.xcodeproj`), la forma más sencilla de ver la app funcionando es:

1.  **Abre Xcode** en tu Mac.
2.  Ve a **File > New > Project**.
3.  Selecciona **iOS** y luego la plantilla **App**. Presiona *Next*.
4.  **Configuración**:
    *   **Product Name**: `CloudStationMobile`
    *   **Interface**: `SwiftUI`
    *   **Language**: `Swift`
    *   Presiona *Next* y guarda el proyecto en la carpeta que prefieras.
5.  **Importar archivos**:
    *   En el panel lateral izquierdo de Xcode, verás archivos por defecto como `ContentView.swift` y `CloudStationMobileApp.swift`. **Bórralos** (puedes moverlos a la papelera).
    *   Arrastra todos los archivos que creé en la carpeta `/Users/subgerenciagrijalva/CFE/CloudStation/CloudStationMobile` (incluyendo la carpeta `Views`) directamente al grupo del proyecto en Xcode.
6.  **¡Listo!**: 
    *   Selecciona un simulador (ej. iPhone 15 Pro) en la parte superior.
    *   Presiona el botón de **Play (Run)**.

### Notas importantes:
- Asegúrate de que el servidor web esté corriendo para que la app pueda recibir datos.
- Si pruebas en un dispositivo real, deberás cambiar `localhost` por la IP local de tu Mac en el archivo `APIService.swift`.
