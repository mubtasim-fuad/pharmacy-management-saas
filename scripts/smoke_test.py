"""Exercise the local SQLite-backed API in CI."""
import json
import time
import urllib.error
import urllib.request

BASE = 'http://127.0.0.1:5075'


def call(method, path, body=None):
    data = None if body is None else json.dumps(body).encode()
    request = urllib.request.Request(
        BASE + path, data=data, headers={'Content-Type': 'application/json'}, method=method)
    try:
        with urllib.request.urlopen(request, timeout=5) as response:
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

assert call('GET', '/api/medicines') == (200, [])
status, medicine = call('POST', '/api/medicines', {
    'name': 'Paracetamol', 'sku': 'p500', 'salePrice': 5, 'reorderLevel': 2})
assert status == 201 and medicine['sku'] == 'P500' and medicine['stock'] == 0, (status, medicine)
id_ = medicine['id']
assert call('POST', '/api/medicines', {
    'name': 'Duplicate', 'sku': 'P500', 'salePrice': 1, 'reorderLevel': 1})[0] == 409
assert call('PUT', f'/api/medicines/{id_}', {
    'name': 'Paracetamol 500 mg', 'sku': 'P500', 'salePrice': 6, 'reorderLevel': 2})[0] == 200

status, purchase = call('POST', '/api/purchases', {
    'supplier': 'Supplier A', 'items': [{'medicineId': id_, 'quantity': 5, 'unitCost': 3}]})
assert status == 201 and purchase['total'] == 15, (status, purchase)
assert call('POST', '/api/purchases', {
    'items': [{'medicineId': id_, 'quantity': 0, 'unitCost': 3}]})[0] == 400
status, sale = call('POST', '/api/sales', {
    'customer': 'Walk-in', 'items': [{'medicineId': id_, 'quantity': 2}]})
assert status == 201 and sale['total'] == 12, (status, sale)
assert call('POST', '/api/sales', {
    'items': [{'medicineId': id_, 'quantity': 4}]})[0] == 409
assert call('GET', '/api/medicines')[1][0]['stock'] == 3, 'Failed sale changed stock'
assert call('GET', '/api/purchases')[1][0]['items'][0]['quantity'] == 5
assert call('GET', '/api/sales')[1][0]['items'][0]['medicineName'] == 'Paracetamol 500 mg'
status, report = call('GET', '/api/reports/summary?days=30')
assert status == 200 and report['revenue'] == 12 and report['costOfGoods'] == 6, report
assert report['stockValue'] == 9 and report['unitsInStock'] == 3, report
print('Smoke test passed: catalog, purchasing, sales, stock guard and report.')
