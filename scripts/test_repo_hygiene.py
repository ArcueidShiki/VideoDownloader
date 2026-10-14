import pathlib
import subprocess
import tempfile
import unittest
import repo_hygiene as hygiene

class HygieneTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.base = pathlib.Path(self.temp.name)
        self.root = self.base / 'repo'
        self.root.mkdir()
        subprocess.run(['git', 'init', '-q', str(self.root)], check=True)
        subprocess.run(['git', '-C', str(self.root), '-c', 'user.name=Test', '-c', 'user.email=test@example.com', 'commit', '--allow-empty', '-qm', 'initial'], check=True)
    def tearDown(self):
        self.temp.cleanup()
    def test_archive_preserves_user_data_and_can_be_restored(self):
        output = self.root / 'macos/.build/cache'
        output.parent.mkdir(parents=True)
        output.write_bytes(b'rebuildable')
        user = self.root / 'downloads/movie.mp4'
        user.parent.mkdir()
        user.write_bytes(b'user data')
        hygiene.archive(self.root, None, False)
        self.assertTrue(output.exists())
        batch = hygiene.archive(self.root, self.base / 'archive', True)
        self.assertEqual((batch / 'macos/.build/cache').read_bytes(), b'rebuildable')
        self.assertEqual(user.read_bytes(), b'user data')
        (batch / 'macos/.build').rename(self.root / 'macos/.build')
        self.assertEqual(output.read_bytes(), b'rebuildable')
    def test_refuses_tracked_content(self):
        path = self.root / 'webapp/dist/valuable.txt'
        path.parent.mkdir(parents=True)
        path.write_text('keep')
        subprocess.run(['git', '-C', str(self.root), 'add', '.'], check=True)
        with self.assertRaises(ValueError): hygiene.archive(self.root, self.base / 'archive', True)
        with self.assertRaises(ValueError): hygiene.check(self.root)
        self.assertTrue(path.exists())
    def test_refuses_symlink_escape(self):
        (self.root / 'macos').symlink_to(self.base, target_is_directory=True)
        (self.base / '.build').mkdir()
        with self.assertRaises(ValueError): hygiene.archive(self.root, self.base / 'archive', True)
    def test_refuses_archive_inside_repository(self):
        (self.root / 'macos/.build').mkdir(parents=True)
        with self.assertRaises(ValueError): hygiene.archive(self.root, self.root / 'archive', True)

if __name__ == '__main__': unittest.main()
