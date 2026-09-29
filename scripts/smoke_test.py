"""Live API smoke test against the disposable PostgreSQL database in CI."""
import json
import time
import urllib.error
import urllib.request

BASE = 'http://127.0.0.1:5075'


def call(method, path, body=None, token=None):
    data = None if body is None else json.dumps(body).encode()
    headers = {'Content-Type': 'application/json'}
    if token:
        headers['Authorization'] = 'Bearer ' + token
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req, timeout=5) as response:
            return response.status, json.load(response)
    except urllib.error.HTTPError as err:
        return err.code, json.load(err) if err.headers.get_content_type() == 'application/json' else {}


for _ in range(40):
    try:
        if call('GET', '/health')[0] == 200:
            break
    except (urllib.error.URLError, TimeoutError):
        time.sleep(1)
else:
    raise AssertionError('API did not become healthy')

status, a = call('POST', '/api/auth/register', {
    'pharmacyName': 'North Pharmacy', 'email': 'north@example.com', 'password': 'test-password-1234'})
assert status == 200, (status, a)
status, b = call('POST', '/api/auth/register', {
    'pharmacyName': 'South Pharmacy', 'email': 'south@example.com', 'password': 'test-password-1234'})
assert status == 200, (status, b)
A, B = a['token'], b['token']
status, med = call('POST', '/api/medicines', {
    'name': 'Paracetamol', 'sku': 'P500', 'salePrice': 5, 'reorderLevel': 2}, A)
assert status == 201, (status, med)
id_ = med['id']
assert call('GET', '/api/medicines', token=B) == (200, []), 'Tenant B can see tenant A inventory'
assert call('POST', '/api/purchases', {
    'supplier': 'Supplier A', 'items': [{'medicineId': id_, 'quantity': 5, 'unitCost': 3}]}, B)[0] == 400
status, purchase = call('POST', '/api/purchases', {
    'supplier': 'Supplier A', 'items': [{'medicineId': id_, 'quantity': 5, 'unitCost': 3}]}, A)
assert status == 201 and purchase['total'] == 15, (status, purchase)
status, sale = call('POST', '/api/sales', {
    'customer': 'Walk-in', 'items': [{'medicineId': id_, 'quantity': 2}]}, A)
assert status == 201 and sale['total'] == 10, (status, sale)
assert call('POST', '/api/sales', {
    'items': [{'medicineId': id_, 'quantity': 4}]}, A)[0] == 409
assert call('POST', '/api/sales', {
    'items': [{'medicineId': id_, 'quantity': 1}]}, B)[0] == 400
assert call('GET', '/api/medicines', token=A)[1][0]['stock'] == 3, 'Failed sale changed stock'
status, report = call('GET', '/api/reports/summary?days=30', token=A)
assert status == 200 and report['revenue'] == 10 and report['costOfGoods'] == 6, report
assert call('GET', '/api/reports/summary', token=B)[1]['revenue'] == 0
print('Smoke test passed: tenant isolation, purchasing, sales, rollback and report.')
