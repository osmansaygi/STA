"""Regression checks for complete, matched recorded nodal As; no CAD host."""
import json
from pathlib import Path
import shutil
import tempfile
import unittest

import numpy as np

from read_gpr_heatmap import read


ROOT=Path(__file__).resolve().parent
SOURCE=ROOT/'sta-copy'/'N_ULK_02'


class RecordedAsTests(unittest.TestCase):
    def setUp(self):
        self.tmp=tempfile.TemporaryDirectory(dir=ROOT)
        self.project=Path(self.tmp.name)/'N_ULK_02'
        for ext in ['.RAD','.GPR','.SO1']:
            shutil.copyfile(SOURCE.with_suffix(ext),self.project.with_suffix(ext))

    def tearDown(self):
        self.tmp.cleanup()

    def edit_gpr(self,func):
        p=self.project.with_suffix('.GPR')
        p.write_bytes(func(p.read_bytes()))

    def test_only_three_existing_files_are_sufficient(self):
        self.assertEqual(len(list(self.project.parent.iterdir())),3)
        data=read(self.project)
        self.assertEqual(data['As'].shape,(733,4))
        self.assertEqual(int(data['connected'].sum()),516)
        self.assertEqual(data['elements'].shape,(488,4))
        ref=json.loads((ROOT/'ui-reference.json').read_text(encoding='utf-8'))
        for node,vals in ref['nodes'].items():
            a=data['As'][int(node)-1]
            np.testing.assert_allclose([max(a[:2]),max(a[2:])],vals[2:],atol=.00001)

    def test_missing_gpr_is_rejected(self):
        self.project.with_suffix('.GPR').unlink()
        with self.assertRaises(FileNotFoundError):read(self.project)

    def test_wrong_coordinate_is_rejected(self):
        self.edit_gpr(lambda b:b.replace(b'\xa1    1\xa1  40\xa1  40\xa1 -0.400',b'\xa1    1\xa1  40\xa1  40\xa1 -0.500',1))
        with self.assertRaisesRegex(ValueError,'coordinates'):read(self.project)

    def test_missing_node_is_rejected(self):
        self.edit_gpr(lambda b:b.replace(b'\xa1    1\xa1  40\xa1',b'\xa1    X\xa1  40\xa1',1))
        with self.assertRaisesRegex(ValueError,'Nonsequential'):read(self.project)

    def test_different_result_peak_is_rejected(self):
        self.edit_gpr(lambda b:b.replace(b'\xa1  10.84\xa1',b'\xa1  10.94\xa1',1))
        with self.assertRaisesRegex(ValueError,'maxima'):read(self.project)

    def test_nonzero_axis_is_rejected(self):
        p=self.project.with_suffix('.SO1');b=bytearray(p.read_bytes());b[:4]=np.array([30],dtype='<f4').tobytes();p.write_bytes(b)
        with self.assertRaisesRegex(ValueError,'axis angle'):read(self.project)

    def test_wrong_project_identity_is_rejected(self):
        self.edit_gpr(lambda b:b.replace(b'(N_ULK_02.ST4)',b'(OTHER.ST4)'))
        with self.assertRaisesRegex(ValueError,'identity'):read(self.project)


if __name__=='__main__':unittest.main()
