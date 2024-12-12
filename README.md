
# Swastha API Service

This project is a part of the Swastha application, focused on the backend API services.

## Prerequisites

- Python 3.10+
- Poetry
- Docker and Docker Compose (for running with Docker)

## Getting Started

### Option 1: Running Locally with Poetry

If you want to run the project locally without Docker, follow these steps:

#### 1. Install Dependencies

Use **Poetry** to install the project dependencies:

```sh
poetry install
```

#### 2. Set Environment Variables

Set the `PYTHONPATH` to ensure all modules are correctly referenced:

For **Windows**:

```sh
set PYTHONPATH=D:\Project\FastApi\bottle\bottle
```

For **macOS/Linux**:

```sh
export PYTHONPATH=/path/to/your/project/bottle/bottle
```

#### 3. Start the Application

Run the application using **Poetry**:

```sh
poetry run start
```

---

### Option 2: Running with Docker Compose

If you want to run the project using Docker Compose, follow these steps:

#### 1. Run Docker Compose

In the root directory of your project, run the following command:

```sh
docker-compose up --build
```

This command will:
- Build the **API service** image using the `Dockerfile`.
- Start both the **API service** and **MongoDB** services.
- Expose the API on port `8000` and MongoDB on port `27017`.

#### 2. Access the Application

Once the services are up and running, you can access your API by navigating to:

```
http://localhost:8000/docs
```

This will bring up the Swagger UI, where you can interact with the API.

---


<!-- ### 5. Initialize the Database

Initialize the database with the required schemas:

```sh
poetry run python api_service\src\db\db_init.py
```

### 6. Run Example Usage

Run the example usage script to verify everything is set up correctly:

```sh
poetry run python api_service\src\db\example_usage.py
``` -->

