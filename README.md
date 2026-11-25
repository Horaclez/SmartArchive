<p align="center"><img src="assets/icon-256.png" width="128" alt="Smart Archive"></p>

# Smart Archive

Sustituto inteligente de WinRAR / 7-Zip para Windows (.NET 8, WPF).

## Funciones

- **Extracción inteligente**: 1 sola carpeta raíz → se extrae directo (sin `App/App`); muchos archivos sueltos → crea una carpeta con el nombre del comprimido. Nunca pisa nada (`Nombre (2)`).
- **Papelera**: tras extraer con éxito, el `.zip/.rar/.7z` va a la Papelera (opcional).
- **Contraseñas**: pide la contraseña al abrir/extraer ZIP, RAR y 7z protegidos; reintenta si es incorrecta y, en lote, reutiliza la última contraseña correcta.
- **Vista previa sin extraer**: texto/código, imágenes y PDF en un panel lateral.
- **Extraer y abrir**: detecta el `.exe` principal y lo lanza (o abre la carpeta).
- **Compresión limpia**: excluye `.git`, `node_modules`, `bin`, `obj`, `__pycache__`, `.tmp`. Formatos **ZIP** y **7z**, con **cifrado AES-256** opcional (en 7z también cifra los nombres).
- **Lote**: arrastra varios comprimidos → extracción en paralelo con una sola barra. Corrige tildes rotas (`á é ñ`).
- **Menú contextual del Explorador** (clic derecho): Extraer aquí / Extraer y abrir / Ver contenido; en carpetas: Comprimir limpio a .zip / .7z. En Windows 11 aparece en «Mostrar más opciones».

## Compilar en tu PC

Requisitos: Windows + [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
# opcional (para crear .7z): descarga 7zr.exe a la carpeta tools/
mkdir tools; Invoke-WebRequest https://www.7-zip.org/a/7zr.exe -OutFile tools/7zr.exe
dotnet run --project src/SmartArchive
```

## Generar el instalador con GitHub (recomendado)

1. Sube este repo a GitHub (rama `main`).
2. Cada push a `main` ejecuta el workflow **Build & Release** y deja el instalador y la versión portable como *artifact* (pestaña **Actions**).
3. Para publicar una versión oficial:
   ```bash
   git tag v1.0.0
   git push origin v1.0.0
   ```
   El workflow crea un **Release** con `SmartArchive-Setup-1.0.0.exe` y `SmartArchive-Portable-1.0.0.zip`.

El instalador es por usuario (no pide administrador), ofrece instalar el menú contextual y se desinstala limpio.

## Compilar el instalador a mano

Instala [Inno Setup 6](https://jrsoftware.org/isinfo.php), publica con
`dotnet publish src/SmartArchive/SmartArchive.csproj -c Release -r win-x64 --self-contained true -o publish`
y compila `installer/SmartArchive.iss`.

## Limitaciones conocidas

- **No crea `.rar`** (formato propietario). Lee RAR, crea ZIP y 7z.
- La verificación de contraseña es de mejor esfuerzo: si pasa la prueba pero falla al extraer, se vuelve a pedir.
- Una app sin firmar muestra el aviso de SmartScreen la primera vez; para evitarlo hace falta un certificado de firma de código.
- La vista previa de PDF requiere el runtime de WebView2 (viene con Windows 10/11 actualizados).

Licencia MIT. Ver `THIRD_PARTY_NOTICES.md`.
