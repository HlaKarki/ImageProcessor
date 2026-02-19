# ImageProcessor

Distributed image processing backend built with .NET Aspire, plus a TanStack Start frontend.

## What this project does

This project lets users upload images, process them asynchronously, and view both technical image outputs and AI analysis.

## Main technologies:

- .NET Aspire (service orchestration)
- ASP.NET Core Web API + JWT auth
- Entity Framework Core + PostgreSQL
- RabbitMQ (async queueing)
- S3/Cloudflare R2 (private object storage with presigned URLs)
- ImageSharp (image transforms and metadata)
- OpenAI API (image AI enrichment)
- Hangfire (scheduled cleanup jobs)
- TanStack Start (frontend)

## Deployment

- Backend: VPS on Hetzner (https://dotnetapi.hla.dev)
- Frontend: Cloudflare (https://dotnetdemo.hla.dev)
- Storage: AWS S3 (primary), Cloudflare R2

## Pipeline summary:

1. User registers/logs in and uploads an image.
2. API stores the original image in S3/R2 and creates a job record in PostgreSQL.
3. API publishes an image processing message to RabbitMQ.
4. Worker generates thumbnails, optimized output, and metadata.
5. Worker publishes an AI analysis message.
6. Worker runs AI analysis (summary, tags, OCR text, safety flags, model/cost metadata).
7. API returns job details to the UI, including presigned read URLs for private storage objects.

## Architecture

- `ImageProcessor.AppHost`: .NET Aspire orchestration for local development.
- `ImageProcessor.ApiService`: REST API, auth, job APIs, presigned URL generation, caching, rate limiting.
- `ImageProcessor.Worker`: background consumers for image processing + AI analysis.
- `ImageProcessor.Data`: EF Core data model and migrations.
- `ImageProcessor.Contracts`: queue message contracts.
- `web`: TanStack Start frontend (Cloudflare deploy target).


## Job model

Each job tracks two stages:

- `Status`: image processing stage (`Pending`, `Processing`, `Completed`, `Error`, ...)
- `AiStatus`: AI stage (`Pending`, `Processing`, `Completed`, `Error`, `Skipped`)

The frontend shows an overall status derived from both.

## API endpoints

- `POST /api/auth/register`
- `POST /api/auth/login`
- `POST /api/images/upload` (JWT required)
- `GET /api/images` (JWT required, paged list)
- `GET /api/images/{jobId}` (JWT required, full detail)


## Storage access model

Objects are kept private in S3/R2. API returns presigned read URLs so the browser can fetch images securely without making buckets public.

## Repo status

`web/README.md` contains TanStack Start-specific notes.
