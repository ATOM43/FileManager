### **Running the File Manager API**

This project provides a File Manager API to handle file uploads, updates, and metadata storage, backed by MongoDB.

---

## **1. Running the API Using Docker Compose**

### Prerequisites
- [Docker](https://www.docker.com/) installed on your system.
- [Docker Compose](https://docs.docker.com/compose/install/) installed.

### Steps
1. Build and start the containers:
   ```bash
   docker-compose up --build
   ```

2. Access the API:
   - Swagger UI: `http://localhost:5000/swagger`
   - API Base URL: `http://localhost:5000`
   - MongoDB: `mongodb://localhost:27017`

3. Stop the containers:
   ```bash
   docker-compose down
   ```

---

## **2. Running the API Locally**

### Prerequisites
- [.NET SDK (9.0 or higher)](https://dotnet.microsoft.com/download) installed.
- MongoDB running locally or accessible remotely.

### Steps

1. **Set Up MongoDB**:
   - Ensure MongoDB is running on `mongodb://localhost:27017` or update the connection string in `Program.cs`:
     ```csharp
     var mongoConnectionString = Environment.GetEnvironmentVariable("MONGO_CONNECTION_STRING") 
                                 ?? "mongodb://localhost:27017";
     ```

2. **Run the API**:
   - Navigate to the project directory and run:
     ```bash
     dotnet run --launch-profile https
     ```

3. **Access the API**:
   - Swagger UI: `https://localhost:<port>/swagger`
   - API Base URL: `https://localhost:<port>`

---

## **Testing the API**

### Endpoints
- **Upload a File**:
  ```bash
  POST /api/files/upload
  ```
  - Attach a `.zip` file as `form-data`.

- **Update a File**:
  ```bash
  POST /api/files/update/{id}
  ```
  - Provide the `id` of the file and a `.zip` file as `form-data`.

- **List Files**:
  ```bash
  GET /api/files/list
  ```

- **Download a File**:
  ```bash
  GET /api/files/download/{id}
  ```

- **Delete a File**:
  ```bash
  DELETE /api/files/delete/{id}
  ```

### Swagger UI
Access API documentation at:
- Docker: `http://localhost:5000/swagger`
- Locally: `https://localhost:<port>/swagger`

---
