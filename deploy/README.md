# Deploy and run QuickHull on Google Kubernetes Engine

This guide deploys PARCS8 in static mode: one GKE master-labeled node runs the Host API, Portal, and database; each worker-labeled node runs one PARCS daemon. An NFS-Ganesha provisioner supplies shared `ReadWriteMany` storage for module uploads and job outputs. The commands below create one master node and four workers so the lab can compare PARCS jobs with 1–4 workers.

Run commands from the project root in PowerShell unless stated otherwise. Review Google Cloud pricing and quotas before creating resources; the cluster, disks, and image storage may incur charges.

## 1. Install tools and configure Google Cloud

Install the .NET 10 SDK, Git, Google Cloud CLI, `kubectl`, Helm, and Docker Desktop. Start Docker Desktop in Linux-container mode. In Google Cloud, select a project with billing enabled and permissions to enable APIs, create GKE and Artifact Registry resources, and grant IAM roles.

Set the values for your project and sign in. Change the region or zone if needed; use the same values in all later commands:

```powershell
$PROJECT_ID = "<YOUR_PROJECT_ID>"
$REGION = "europe-central2"
$ZONE = "europe-central2-a"
$CLUSTER = "quickhull-parcs"
$REGISTRY = "$REGION-docker.pkg.dev/$PROJECT_ID/parcs"

gcloud auth login
gcloud config set project $PROJECT_ID
gcloud services enable compute.googleapis.com container.googleapis.com artifactregistry.googleapis.com cloudbuild.googleapis.com --project=$PROJECT_ID
```

Create the Docker Artifact Registry once. If a repository named `parcs` already exists in this project and region, skip this command:

```powershell
gcloud artifacts repositories create parcs --repository-format=docker --location=$REGION --project=$PROJECT_ID
```

## 2. Create the GKE cluster

Create the master-labeled node pool, then the four-node worker pool:

```powershell
gcloud container clusters create $CLUSTER `
  --project=$PROJECT_ID --zone=$ZONE --num-nodes=1 `
  --machine-type=e2-standard-2 --node-labels=parcs-role=master `
  --enable-ip-alias

gcloud container node-pools create workers `
  --cluster=$CLUSTER --project=$PROJECT_ID --zone=$ZONE `
  --num-nodes=4 --machine-type=e2-standard-2 `
  --node-labels=parcs-role=worker

gcloud container clusters get-credentials $CLUSTER --zone=$ZONE --project=$PROJECT_ID
kubectl get nodes --show-labels
```

Confirm that one node has `parcs-role=master` and four have `parcs-role=worker`. Allow the nodes' service account to pull private images. This uses the default Compute Engine service account; if your cluster uses a custom node service account, substitute its email:

```powershell
$PROJECT_NUMBER = gcloud projects describe $PROJECT_ID --format="value(projectNumber)"
$NODE_SA = "$PROJECT_NUMBER-compute@developer.gserviceaccount.com"
gcloud artifacts repositories add-iam-policy-binding parcs `
  --location=$REGION --project=$PROJECT_ID `
  --member="serviceAccount:$NODE_SA" `
  --role=roles/artifactregistry.reader
```

## 3. Get PARCS8 source and build the images

The platform build uses this project's pinned PARCS8 commit and a small compatibility patch that makes Elasticsearch logging optional. The patch keeps console logging enabled and avoids deploying Elasticsearch. Run from the project root:

```powershell
git clone https://github.com/alexeybogusevich/parcs8 third_party/parcs8
git -C third_party/parcs8 checkout d35d29f948cb1849a070967e681bc0df7f25d779
git -C third_party/parcs8 apply ../../patches/0001-optional-elasticsearch-logging.patch
```

Build the platform images, package PARCS.Net into the local feed, and publish the QuickHull module:

```powershell
docker build -f docker/parcs8.Dockerfile --target daemon -t "$REGISTRY/daemon:lab" third_party/parcs8
docker build -f docker/parcs8.Dockerfile --target host -t "$REGISTRY/host:lab" third_party/parcs8
docker build -f docker/parcs8.Dockerfile --target portal -t "$REGISTRY/portal:lab" third_party/parcs8

dotnet pack third_party/parcs8/src/Parcs.Net/Parcs.Net.csproj -c Release -o artifacts/nuget -p:GeneratePackageOnBuild=false
$sources = dotnet nuget list source
if ($sources -notmatch 'parcs-local') {
    dotnet nuget add source "$PWD/artifacts/nuget" --name parcs-local
}
dotnet publish modules/QuickHull.PARCS/QuickHull.PARCS.csproj -c Release

docker build -f docker/QuickHull.Sequential.Dockerfile -t "$REGISTRY/quickhull-sequential:lab" .
gcloud auth configure-docker "$REGION-docker.pkg.dev"
docker push "$REGISTRY/daemon:lab"
docker push "$REGISTRY/host:lab"
docker push "$REGISTRY/portal:lab"
docker push "$REGISTRY/quickhull-sequential:lab"
```

PARCS.Net 10.1.0 is packed from the pinned source because the required version is not available from nuget.org. The Dockerfile disables `GeneratePackageOnBuild` for the explicit package step to avoid the `NU5026` build error. The module output is under `modules/QuickHull.PARCS/bin/Release/net10.0/publish/`.

## 4. Install shared storage and deploy PARCS

Install the NFS provisioner. Wait for Helm to finish and verify that it created the `parcs-rwx` StorageClass:

```powershell
helm repo add nfs-ganesha-server-and-external-provisioner https://kubernetes-sigs.github.io/nfs-ganesha-server-and-external-provisioner/
helm repo update
helm upgrade --install nfs-server nfs-ganesha-server-and-external-provisioner/nfs-server-provisioner --version 1.8.0 --namespace default --create-namespace --values deploy/gcp/nfs-values.yaml
kubectl get storageclass parcs-rwx
```

Deploy the PARCS services and wait for all four deployments to become ready:

```powershell
./deploy/scripts/deploy-gcp.ps1 -ProjectId $PROJECT_ID -Region $REGION -ImageTag lab
kubectl get nodes -o wide
kubectl get deployments,pods,pvc -o wide
```

The script creates a random database password in a Kubernetes Secret, fills in the registry and tag in a temporary copy of the overlay, applies the manifests, and waits for the daemon, Host API, Portal, and database deployments. The tracked manifests contain no project-specific registry path or password.

## 5. Upload and run the PARCS module

Forward the internal Portal service to your machine. Keep this PowerShell window open while using the Portal:

```powershell
kubectl port-forward service/parcs-portal 8080:80
```

Open `http://localhost:8080`, upload both `QuickHull.PARCS.dll` and `QuickHull.PARCS.deps.json` from the publish directory, then create a job with:

- Assembly: `QuickHull.PARCS`
- Class: `QuickHull.PARCS.MainModule`
- Arguments (JSON): `{"PointsNumber":4,"PointCount":500000,"Seed":42,"Warmup":1,"Repetitions":5}`

For the worker-count comparison, run the same job for each `PointsNumber`: **1, 2, 3, and 4**. Keep `PointCount`, `Seed`, `Warmup`, and `Repetitions` identical for every run. Each job returns `timings.json` and `hull.json`. `SetupMilliseconds` separates PARCS setup from the measured QuickHull rounds.

## 6. Run the sequential baseline

Run the sequential Job on a worker node with the same point count, seed, warmup, and repetitions as the PARCS jobs. The Job requests 500m CPU so it can schedule beside the daemon on an e2-standard-2 worker and has a 1-vCPU limit matching the daemon's compute cap:

```powershell
./deploy/scripts/run-sequential.ps1 -ProjectId $PROJECT_ID -PointCount 500000 -Seed 42 -Warmup 1 -Repetitions 5
```

The script waits for completion and prints the JSON result from the Job logs. Repeat for every point count used in PARCS (for example, use `-PointCount 2000000` for a second workload). Compare the median QuickHull times and check that the sequential and parallel hull coordinates match. The script gives each Kubernetes Job a unique name; remove completed Jobs after collecting their outputs if desired:

```powershell
kubectl get jobs
kubectl delete job <JOB_NAME>
```

## 7. Stop and clean up resources

When finished with the lab, remove its cloud resources. Check PVCs and persistent disks before deleting them; the NFS Helm release can leave a volume behind:

```powershell
kubectl delete -k deploy/gcp
helm uninstall nfs-server
kubectl get pvc
gcloud compute disks list --project=$PROJECT_ID
gcloud container clusters delete $CLUSTER --zone=$ZONE --project=$PROJECT_ID
```

Delete any remaining lab disks or the Artifact Registry repository separately only after confirming they are not used by other workloads.
