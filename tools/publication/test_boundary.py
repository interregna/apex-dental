"""Regression checks for publication leaks; all inputs are synthetic."""
import unittest
from check_boundary import inspect


class PublicationBoundaryTests(unittest.TestCase):
    def test_secrets_in_ordinary_source_are_blocked(self):
        token = ('AK' + 'IA' + 'X' * 16).encode()
        errors = inspect('OpenDentalWebDemo/Example.cs', b'var key="' + token + b'";')
        self.assertTrue(any('AWS access key' in error for error in errors))
        self.assertTrue(all(token.decode() not in error for error in errors))

    def test_private_directories_and_database_are_blocked(self):
        for name in ['docs/private/plan.md', '.env.production', 'login.txt',
                     'docs/patients.sql', 'OpenDentalWebDemo/bin/app.dll',
                     'private/Program.cs', 'unapproved/scripts/task.py']:
            with self.subTest(name=name):
                self.assertTrue(inspect(name, b''))

    def test_cloud_implementation_is_blocked(self):
        self.assertTrue(inspect('OpenDentalWebDemo/App.csproj', b'<PackageReference Include="AWSSDK.S3"/>'))
        self.assertTrue(inspect('OpenDentalWebDemo/Provider.cs', b'using Amazon.S3;'))

    def test_public_source_and_placeholder_config_are_allowed(self):
        self.assertEqual([], inspect('OpenDentalWebDemo/Program.cs', b'// ordinary source'))
        self.assertEqual([], inspect('.env.example', b'OD_DB_PASSWORD=replace-me'))
        self.assertEqual([], inspect('OpenDentBusiness/WebTypes/PatientPortalCredential.cs', b'// public data transfer object'))
        self.assertEqual([], inspect('OpenDental/Resources/SharpDX.txt', b'License text'))

class ServiceBoundaryTests(unittest.TestCase):
    def test_all_vendor_sdks_are_blocked_in_build_inputs(self):
        for sdk in ['Google.Apis', 'Dropbox.Api', 'Azure.Core', 'Microsoft.Graph',
                    'Microsoft.Identity.Client', 'Intuit.Ipp.Core', 'Avalara.AvaTax.RestClient']:
            with self.subTest(sdk=sdk):
                self.assertTrue(inspect('OpenDentalWebDemo/Engine.csproj',
                    ('<Project><PackageReference Include="'+sdk+'"/></Project>').encode()))
                self.assertTrue(inspect('dependencies.json',sdk.encode()))

    def test_private_source_cannot_be_linked_by_a_public_project(self):
        self.assertTrue(inspect('OpenDentBusinessCore/App.csproj',
            b'<Project><Compile Include="../../private/Engine.cs"/></Project>'))

    def test_allowed_directory_does_not_authorize_new_or_changed_files(self):
        import hashlib
        import json
        from check_boundary import inspect_inventory
        original=b'public source'
        manifest=json.dumps({'schema':1,'scope':'practice-software','files':{
            'OpenDentalWebDemo/Program.cs':hashlib.sha256(original).hexdigest()}}).encode()
        good={'public-files.json':manifest,'OpenDentalWebDemo/Program.cs':original}
        self.assertEqual([],inspect_inventory(good))
        for name in ['OpenDentalWebDemo/Modules/NewService.cs','docs/internal-strategy.md']:
            self.assertTrue(inspect_inventory({**good,name:b'internal implementation'}))
        self.assertTrue(inspect_inventory({**good,'OpenDentalWebDemo/Program.cs':b'internal implementation'}))

class SnapshotTests(unittest.TestCase):
    def test_default_check_catches_unstaged_and_untracked_source(self):
        import contextlib,hashlib,io,json,subprocess,tempfile
        from pathlib import Path
        from unittest.mock import patch
        import check_boundary
        with tempfile.TemporaryDirectory() as temporary:
            root=Path(temporary);subprocess.run(['git','init','-q',temporary],check=True)
            (root/'README.md').write_text('Reviewed source\n')
            manifest={'schema':1,'scope':'practice-software','files':{'README.md':hashlib.sha256((root/'README.md').read_bytes()).hexdigest()}}
            (root/'public-files.json').write_text(json.dumps(manifest))
            subprocess.run(['git','add','.'],cwd=root,check=True)
            with patch.object(check_boundary,'ROOT',root),contextlib.redirect_stdout(io.StringIO()):
                self.assertEqual(0,check_boundary.main([]))
                (root/'README.md').write_text('Unreviewed modification\n')
                self.assertEqual(1,check_boundary.main([]))
                self.assertEqual(0,check_boundary.main(['--source','index']))
                (root/'README.md').write_text('Reviewed source\n')
                (root/'docs').mkdir();(root/'docs/new.md').write_text('Unreviewed new source')
                self.assertEqual(1,check_boundary.main([]))


if __name__=='__main__':
    unittest.main()
