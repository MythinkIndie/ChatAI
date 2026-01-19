# ChatAI

**ChatAI** es una aplicación de escritorio de chat impulsada por IA, desarrollada en **C# con .NET/WPF**. Esta herramienta permite mantener conversaciones tipo chatbot, almacenar un historial de chats y extender el proyecto fácilmente.

> 🧠 *Actualmente no tiene una descripción oficial en el repositorio, así que este README sirve para documentar cómo iniciar y entender el proyecto.*

---

## 📌 ¿Qué es ChatAI?

ChatAI es una aplicación cliente de chat con inteligencia artificial. El proyecto está construido con tecnologías de **.NET y WPF (Windows Presentation Foundation)** y utiliza un contexto local (`ChatDbContext`) para guardar el historial de conversaciones.  
La interfaz está diseñada como una app de escritorio tradicional para Windows.

---

## 🚀 Características

- 🖥️ Interfaz de usuario nativa usando WPF
- 💬 Chat con IA
- 📚 Historial de conversaciones persistente
- 📂 Aplicación local sin dependencia directa de servicios externos (configurable)
- 🛠️ Proyecto abierto y modificable para distintos usos

---

## 📁 Estructura del Repositorio

```text
/
├── img/ 📸 Recursos de imágenes
├── App.xaml 💡 Configuración de la app WPF
├── MainWindow.xaml 🪟 Interfaz principal
├── HistoryWindow.xaml 🗂️ Ventana de historial
├── ChatDbContext.cs 🛢️ Contexto de base de datos
├── ChatAI.csproj 📦 Proyecto C# .NET
├── ChatAI.slnx 📐 Solución de Visual Studio
├── MarkdownStyles.xaml 🎨 Estilos para mostrar markdown
├── chatapp.db 🧾 Base de datos SQLite incluida
└── README.md 📄 Esta documentación
```

---

## 🧰 Tecnologías

Este proyecto está construido con:

- **C#**
- **.NET (Framework o Core según tu versión)**
- **WPF – Windows Presentation Foundation**
- **SQLite** para persistencia de datos

---

## 🛠️ Requisitos

Antes de compilar o ejecutar:

- 📦 Tener instalado Visual Studio (2019 o superior) o .NET SDK
- 🧩 La versión de .NET necesaria según el archivo `.csproj`
- 🪟 Windows (la app WPF está orientada a sistemas Windows)

---

## 🏁 Cómo ejecutar (desde Visual Studio)

1. **Clona el repositorio**

   ```bash
   git clone https://github.com/MythinkIndie/ChatAI.git
   cd ChatAI
   
2. **Abre la solución**

  Abre ChatAI.slnx en Visual Studio.
   
3. **Restaurar dependencias**

   Si Visual Studio lo solicita, restaura los paquetes NuGet.
   
4. **Compilar y ejecutar**

   Presiona F5 o usa “Start” para compilar y levantar la aplicación.
