#!/usr/bin/env python3
"""Render the private compatibility slice; kubectl owns application resources.
Secrets are installed separately from protected files. No cloud calls here.
"""
import argparse
import ipaddress
import json
import pathlib
import re


def render(inventory, image):
    campaign = inventory['campaign']
    if not re.fullmatch(r'premise-compat-[a-z0-9-]{1,24}', campaign):
        raise ValueError('invalid campaign name')
    if not re.fullmatch(r'[^\s]+@sha256:[0-9a-f]{64}', image):
        raise ValueError('application image must be pinned by digest')
    ipaddress.ip_address(inventory['nfs_host'])
    if not inventory['nfs_path'].startswith('/'):
        raise ValueError('NFS export path must be absolute')
    resources = [{'apiVersion': 'v1', 'kind': 'Namespace', 'metadata': {'name': campaign}}]

    def resource(kind, name, spec=None, **extra):
        obj = {'apiVersion': 'apps/v1' if kind == 'Deployment' else 'batch/v1' if kind == 'Job' else 'v1',
               'kind': kind, 'metadata': {'name': name, 'namespace': campaign}, **extra}
        if spec is not None:
            obj['spec'] = spec
        resources.append(obj)

    def pod(role):
        return {'automountServiceAccountToken': False,
                'imagePullSecrets': [{'name': 'registry'}],
                'securityContext': {'runAsNonRoot': True, 'runAsUser': 1654, 'runAsGroup': 1654, 'fsGroup': 1654},
                'containers': [{'name': 'premise', 'image': image,
                    'securityContext': {'allowPrivilegeEscalation': False, 'capabilities': {'drop': ['ALL']}},
                    'envFrom': [{'secretRef': {'name': 'providers'}},
                                {'secretRef': {'name': 'migration-database' if role == 'migrate' else 'app-database'}}],
                    'env': [{'name': k, 'value': v} for k, v in {
                        'ROLE': role, 'ASPNETCORE_ENVIRONMENT': 'Production',
                        'ASPNETCORE_URLS': 'http://+:8080',
                        'DataProtection__KeyPath': '/keys',
                        'DataProtection__CertificatePath': '/certificate/keyring.pfx',
                        'Gateway__Required': 'true', 'Traffic__MaxConcurrentRequests': '32',
                    }.items()],
                    'ports': [{'containerPort': 8080}],
                    'resources': {'requests': {'cpu': '500m', 'memory': '512Mi'},
                                  'limits': {'cpu': '1', 'memory': '2Gi'}},
                    'volumeMounts': [{'name': 'keys', 'mountPath': '/keys'},
                                     {'name': 'certificate', 'mountPath': '/certificate', 'readOnly': True},
                                     {'name': 'database-ca', 'mountPath': '/database-ca', 'readOnly': True}]}],
                'volumes': [{'name': 'keys', 'nfs': {'server': inventory['nfs_host'], 'path': inventory['nfs_path']}},
                            {'name': 'certificate', 'secret': {'secretName': 'keyring-certificate', 'defaultMode': 0o440}},
                            {'name': 'database-ca', 'secret': {'secretName': 'database-ca'}}]}

    migrate = pod('migrate')
    migrate['restartPolicy'] = 'Never'
    resource('Job', 'migrate', {'backoffLimit': 0, 'activeDeadlineSeconds': 600,
                              'template': {'metadata': {'labels': {'app': 'migrate'}}, 'spec': migrate}})
    for role, replicas in [('api', 2), ('worker', 1)]:
        spec = pod(role)
        spec['terminationGracePeriodSeconds'] = 120
        spec['containers'][0].update({
            'startupProbe': {'httpGet': {'path': '/livez', 'port': 8080}, 'periodSeconds': 2, 'failureThreshold': 90},
            'readinessProbe': {'httpGet': {'path': '/healthz', 'port': 8080}, 'periodSeconds': 5},
            'livenessProbe': {'httpGet': {'path': '/livez', 'port': 8080}, 'periodSeconds': 10}})
        if role == 'api':
            spec['affinity'] = {'podAntiAffinity': {'requiredDuringSchedulingIgnoredDuringExecution': [
                {'labelSelector': {'matchLabels': {'app': 'api'}}, 'topologyKey': 'kubernetes.io/hostname'}]}}
        resource('Deployment', role, {'replicas': replicas, 'selector': {'matchLabels': {'app': role}},
            # Two fixed nodes cannot schedule a third anti-affine API. This private
            # bootstrap uses Recreate; rolling-update qualification is a later slice.
            'strategy': {'type': 'Recreate'},
            'template': {'metadata': {'labels': {'app': role}}, 'spec': spec}})
    resource('Service', 'api', {'clusterIP': 'None', 'selector': {'app': 'api'},
                              'ports': [{'port': 8080, 'targetPort': 8080}]})
    return {'apiVersion': 'v1', 'kind': 'List', 'items': resources}


def check():
    sample = {'campaign': 'premise-compat-check', 'nfs_host': '10.0.0.2', 'nfs_path': '/keys'}
    image = 'example.test/premise@sha256:' + 'a' * 64
    items = render(sample, image)['items']
    job = next(x for x in items if x['kind'] == 'Job')
    api = next(x for x in items if x['kind'] == 'Deployment' and x['metadata']['name'] == 'api')
    assert 'migration-database' in json.dumps(job)
    assert 'migration-database' not in json.dumps(api)
    assert api['spec']['replicas'] == 2 and 'requiredDuringSchedulingIgnoredDuringExecution' in json.dumps(api)
    assert all(x['spec'].get('type') != 'LoadBalancer' for x in items if x['kind'] == 'Service')
    for invalid in ['example.test/premise:latest', 'example.test/premise@sha256:bad']:
        try:
            render(sample, invalid)
        except ValueError:
            pass
        else:
            raise AssertionError('unpinned image accepted')
    print('private topology, credential separation and image checks passed')


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--check', action='store_true')
    parser.add_argument('--inventory', type=pathlib.Path)
    parser.add_argument('--image')
    parser.add_argument('--phase', choices=['migrate', 'workloads'], required=False)
    args = parser.parse_args()
    if args.check:
        check()
    else:
        if not args.inventory or not args.image:
            parser.error('--inventory and --image are required')
        if not args.phase:
            parser.error('--phase migrate or workloads is required; migration must complete first')
        result = render(json.loads(args.inventory.read_text()), args.image)
        result['items'] = [x for x in result['items'] if (x['kind'] in ['Namespace', 'Job']) == (args.phase == 'migrate')]
        print(json.dumps(result, indent=2))
