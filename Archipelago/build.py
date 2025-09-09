import shutil
shutil.make_archive('dawnsbury', 'zip', root_dir='.', base_dir='dawnsbury')
shutil.move('dawnsbury.zip', 'dawnsbury.apworld')